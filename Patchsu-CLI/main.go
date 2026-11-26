// Patchsu-CLI picks Patchsu patches, checks the system, fetches what's missing and builds the patched osu!lazer.
package main

import (
	"archive/zip"
	"context"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"os/exec"
	"os/signal"
	"path/filepath"
	"strings"
	"time"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/build"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/bundle"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/config"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/deps"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/release"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/tui"
)

// Version is set at release time with -ldflags "-X main.Version=...".
var Version = "1.1"

// The Windows icon and version info of Patchsu-CLI.exe (rsrc_windows_*.syso, committed).
//go:generate go run github.com/tc-hib/go-winres@v0.3.3 simply --icon internal/bundle/logo/patchsu.ico --arch amd64,arm64 --manifest cli --product-name Patchsu-CLI --file-description "Patchsu-CLI: builds osu!lazer with Patchsu patches" --original-filename Patchsu-CLI.exe

const usage = `Patchsu-CLI: build osu!lazer with the Patchsu patches you choose.

Usage:
  Patchsu-CLI [flags]                 interactive: pick patches, check the system, build
  Patchsu-CLI build [flags]           build without the menus (--preset or --patches)
  Patchsu-CLI list [flags]            show patches, what they need, the presets and the custom servers
  Patchsu-CLI check [flags]           only check what's needed (exit code 1 if something is missing)
  Patchsu-CLI uninstall [--server NAME] [--prefix DIR]
                                      remove the installed game and its launcher (osu! data is untouched)
  Patchsu-CLI make-release --local DIR --tag TAG --out DIR
                                      write manifest.json + patches.zip for a GitHub release

Flags:
  --preset NAME      build: use a preset ("Vanilla+", "Local play", "Custom server")
  --patches LIST     build: comma-separated patch ids; what they need is added
  --server NAME      build: the custom server for the "Custom server" preset; the game is built as Patchsu-NAME
                     uninstall: remove Patchsu-NAME instead of Patchsu
  --yes              build: approve downloads without asking
  --release TAG      use this patch release from GitHub instead of the built-in patches ("latest": the newest,
                     "main": the patches on the repo's main branch)
  --no-update        use the built-in patches even when the patches on GitHub changed after this CLI was built
                     (normally the newer ones are used, so you don't need a new Patchsu-CLI for patch updates)
  --local DIR        use a local patches folder (manifest.json or patches.meta.json)
  --data-dir DIR     where downloads and build files go (default: remembered choice)
  --prefix DIR       install into exactly this folder (the .app on macOS). Without it, the game goes where the
                     interactive CLI's install location says (i on the check screen), else the standard place
  --no-launcher      don't create the menu entry / Start Menu shortcut
  --version          print the version
`

type flags struct {
	preset, patches, server, release, local, dataDir, prefix, tag, out string
	yes, noLauncher, noUpdate, version                                 bool
}

func parse(name string, args []string) (*flags, error) {
	f := &flags{}
	fs := flag.NewFlagSet(name, flag.ContinueOnError)
	fs.SetOutput(io.Discard)
	fs.StringVar(&f.preset, "preset", "", "")
	fs.StringVar(&f.patches, "patches", "", "")
	fs.StringVar(&f.server, "server", "", "")
	fs.StringVar(&f.release, "release", "", "")
	fs.StringVar(&f.local, "local", "", "")
	fs.StringVar(&f.dataDir, "data-dir", "", "")
	fs.StringVar(&f.prefix, "prefix", "", "")
	fs.StringVar(&f.tag, "tag", "", "")
	fs.StringVar(&f.out, "out", "", "")
	fs.BoolVar(&f.yes, "yes", false, "")
	fs.BoolVar(&f.yes, "y", false, "")
	fs.BoolVar(&f.noLauncher, "no-launcher", false, "")
	fs.BoolVar(&f.noUpdate, "no-update", false, "")
	fs.BoolVar(&f.version, "version", false, "")
	if err := fs.Parse(args); err != nil {
		return nil, err
	}
	if fs.NArg() > 0 {
		return nil, fmt.Errorf("unexpected argument %q", fs.Arg(0))
	}
	return f, nil
}

func main() {
	args := os.Args[1:]
	cmd := ""
	if len(args) > 0 && !strings.HasPrefix(args[0], "-") {
		cmd, args = args[0], args[1:]
	}
	if cmd == "help" || (len(args) > 0 && (args[0] == "-h" || args[0] == "--help")) {
		fmt.Print(usage)
		return
	}
	f, err := parse(cmd, args)
	if err != nil {
		fail(err.Error() + "\n\n" + usage)
	}
	if f.version {
		fmt.Println("Patchsu-CLI", Version)
		return
	}
	if f.dataDir != "" {
		if f.dataDir, err = filepath.Abs(f.dataDir); err != nil {
			fail(err.Error())
		}
	}
	// A patches folder next to the program (or the repo's Patchsu/patches for the dev build in Patchsu-CLI/) is used automatically,
	// unless a release or another folder was asked for.
	if f.local == "" && f.release == "" && cmd != "make-release" && cmd != "uninstall" {
		f.local = besideExecutable()
	}

	switch cmd {
	case "":
		err = tui.Run(tui.Options{Version: Version, Tag: f.release, PatchesDir: f.local, Builtin: bundle.Patches(),
			DataDir: f.dataDir, Prefix: f.prefix, NoLauncher: f.noLauncher, NoUpdate: f.noUpdate})
	case "list":
		err = cmdList(f)
	case "check":
		err = cmdCheck(f)
	case "build":
		err = cmdBuild(f)
	case "uninstall":
		err = cmdUninstall(f)
	case "make-release":
		err = cmdMakeRelease(f)
	default:
		err = fmt.Errorf("unknown command %q\n\n%s", cmd, usage)
	}
	if err != nil {
		fail(err.Error())
	}
}

// besideExecutable returns <program folder>/patches, or <program folder>/../Patchsu/patches (the dev build that
// install-dev.sh puts into Patchsu-CLI/), when it has manifest.json or patches.meta.json, else "".
func besideExecutable() string {
	exe, err := os.Executable()
	if err != nil {
		return ""
	}
	if resolved, err := filepath.EvalSymlinks(exe); err == nil {
		exe = resolved
	}
	here := filepath.Dir(exe)
	for _, dir := range []string{filepath.Join(here, "patches"), filepath.Join(here, "..", "Patchsu", "patches")} {
		for _, name := range []string{release.ManifestAsset, release.MetaFile} {
			if _, err := os.Stat(filepath.Join(dir, name)); err == nil {
				return filepath.Clean(dir)
			}
		}
	}
	return ""
}

func fail(msg string) {
	fmt.Fprintln(os.Stderr, "\033[1;31mError:\033[0m "+msg)
	os.Exit(1)
}

func say(format string, a ...any) { fmt.Printf("\033[1;35m==>\033[0m "+format+"\n", a...) }

// dataDir returns --data-dir, else the remembered choice, else Documents/patchsu-data (non-interactive default).
func dataDir(f *flags, info *sysinfo.Info, cfg *config.Config) string {
	switch {
	case f.dataDir != "":
		return f.dataDir
	case cfg.DataDir != "":
		return cfg.DataDir
	}
	return filepath.Join(info.DocumentsDir(), paths.DataDirName)
}

// loadSource: --local, else --release from GitHub, else the built-in patches (or newer patches from the repo's main
// branch or a patch release, unless --no-update), else the newest GitHub release.
func loadSource(f *flags, cacheDir string) (*release.Source, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
	defer cancel()
	src, err := release.Open(ctx, f.local, f.release, bundle.Patches(), cacheDir)
	if err != nil {
		return nil, err
	}
	if !f.noUpdate && src.Origin == "builtin" {
		uctx, ucancel := context.WithTimeout(context.Background(), release.UpdateTimeout)
		up, notice := release.Update(uctx, src, cacheDir, Version)
		ucancel()
		if notice != "" {
			say("%s", notice)
		}
		if up != nil {
			src = up
		}
	}
	return src, src.Manifest.CheckCLI(Version)
}

func cmdList(f *flags) error {
	info, cfg := sysinfo.Detect(), config.Load()
	src, err := loadSource(f, paths.Data{Root: dataDir(f, info, cfg)}.Releases())
	if err != nil {
		return err
	}
	m := src.Manifest
	fmt.Printf("%s · osu!lazer %s · .NET %s\n\nPatches:\n", src.Describe(), m.OsuCommit[:10], m.DotnetSDK)
	for _, p := range m.Patches {
		needs := ""
		if p.Required {
			needs = "required"
		} else if len(p.Needs) > 0 {
			needs = "needs " + strings.Join(p.Needs, ", ")
		}
		if sv := m.ServerPatch(p.ID); sv != nil {
			needs += " (comes with server " + sv.Name + ")"
		}
		fmt.Printf("  %-5s  %-32s %s\n", p.ID, p.Name, needs)
	}
	fmt.Println("\nPresets:")
	for i := range m.Presets {
		p := &m.Presets[i]
		ids := strings.Join(m.IDs(m.PresetSelection(p)), " ")
		if p.Server {
			ids += " + the server's patches"
		}
		fmt.Printf("  %-28s %s\n  %-28s %s\n", p.Name, ids, "", p.Description)
	}
	fmt.Println("\nCustom servers (for the server presets, built as Patchsu-<Server>):")
	if len(m.Servers) == 0 {
		fmt.Println("  none yet")
	}
	for _, sv := range m.Servers {
		fmt.Printf("  %-28s %s\n  %-28s %s\n", sv.Name, strings.Join(sv.Patches, " "), "", sv.Description)
	}
	return nil
}

// pickServer returns the server named by --server, or the only one there is. want says whether the build needs one.
func pickServer(m *manifest.Manifest, name string, want bool) (*manifest.Server, error) {
	if name != "" {
		sv := m.Server(name)
		if sv == nil {
			names := []string{}
			for _, s := range m.Servers {
				names = append(names, s.Name)
			}
			if len(names) == 0 {
				return nil, fmt.Errorf("unknown server %q: no custom server patches are published yet", name)
			}
			return nil, fmt.Errorf("unknown server %q (servers: %s)", name, strings.Join(names, ", "))
		}
		return sv, nil
	}
	switch {
	case !want:
		return nil, nil
	case len(m.Servers) == 0:
		return nil, errors.New("no custom server patches are published yet, so a server build has nothing to connect to")
	case len(m.Servers) == 1:
		return &m.Servers[0], nil
	}
	return nil, errors.New("this preset needs --server NAME (see `Patchsu-CLI list`)")
}

func printReport(r *deps.Report) {
	fmt.Println("Detected:", r.Info.Summary())
	for _, it := range r.Items {
		mark := "\033[32m✓\033[0m"
		if !it.OK {
			mark = "\033[31m✗\033[0m"
		}
		fmt.Printf("  %s %-26s %s\n", mark, it.Name, it.Detail)
	}
	free := "unknown"
	if r.Free >= 0 {
		free = download.HumanBytes(r.Free)
	}
	fmt.Printf("  Disk space: ~%s needed, %s free\n", download.HumanBytes(r.Needed), free)
}

func cmdCheck(f *flags) error {
	info, cfg := sysinfo.Detect(), config.Load()
	data := paths.Data{Root: dataDir(f, info, cfg)}
	src, err := loadSource(f, data.Releases())
	if err != nil {
		return err
	}
	r := deps.Check(context.Background(), info, src, data)
	printReport(r)
	if !r.Ready() {
		os.Exit(1)
	}
	return nil
}

func cmdBuild(f *flags) error {
	info, cfg := sysinfo.Detect(), config.Load()
	data := paths.Data{Root: dataDir(f, info, cfg)}
	src, err := loadSource(f, data.Releases())
	if err != nil {
		return err
	}
	m := src.Manifest

	var sel manifest.Selection
	preset := ""
	switch {
	case f.preset != "" && f.patches != "":
		return errors.New("use either --preset or --patches, not both")
	case f.preset != "":
		p := m.Preset(f.preset)
		if p == nil {
			names := []string{}
			for _, pr := range m.Presets {
				names = append(names, pr.Name)
			}
			return fmt.Errorf("unknown preset %q (presets: %s)", f.preset, strings.Join(names, ", "))
		}
		sv, err := pickServer(m, f.server, p.Server)
		if err != nil {
			return err
		}
		if sv != nil && !p.Server {
			return fmt.Errorf("preset %q doesn't take a server; use a server preset (see `Patchsu-CLI list`)", p.Name)
		}
		sel, preset = m.PresetSelectionFor(p, sv), p.Name
	case f.patches != "":
		var added []string
		sel, added, err = m.Resolve(strings.Split(f.patches, ","))
		if err != nil {
			return err
		}
		if f.server != "" {
			sv, err := pickServer(m, f.server, true)
			if err != nil {
				return err
			}
			if other := m.ServerOf(sel); other != nil && other != sv {
				return fmt.Errorf("--patches has %s's patches, but --server is %s", other.Name, sv.Name)
			}
			m.SetServer(sel, sv)
		}
		if len(added) > 0 {
			say("Also enabled %d (needed): %s", len(added), m.Labels(added))
		}
		if p := m.MatchPreset(sel); p != nil {
			preset = p.Name
		}
	default:
		return errors.New("build needs --preset NAME or --patches LIST (or run Patchsu-CLI without a command for the menus)")
	}
	name := m.BuildName(sel)
	say("%s · %s · %d patches: %s", name, src.Describe(), len(m.IDs(sel)), strings.Join(m.IDs(sel), " "))

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()

	r := deps.Check(ctx, info, src, data)
	printReport(r)
	if !r.DiskOK() {
		return errors.New("not enough free disk space")
	}
	attempted := map[string]bool{}
	for !r.Ready() {
		var next *deps.Item
		for _, key := range []string{"git", "dotnet", "source", "patches"} {
			for i := range r.Items {
				it := &r.Items[i]
				if it.Key == key && !it.OK && next == nil && !(key == "source" && r.Tools.Git == "") {
					next = it
				}
			}
		}
		if next == nil {
			return errors.New("git is needed before the osu! source can be downloaded")
		}
		if next.Fix == nil || next.Fix.Manual != "" {
			msg := next.Name + " is missing"
			if next.Fix != nil {
				msg += ": " + next.Fix.Manual
			}
			return errors.New(msg)
		}
		if attempted[next.Key] {
			return fmt.Errorf("%s is still missing after trying to get it", next.Name)
		}
		attempted[next.Key] = true
		if !f.yes && !confirm(fmt.Sprintf("Get %s (%s)?", next.Name, next.Fix.Summary)) {
			return errors.New("cancelled")
		}
		if next.Fix.Command != nil {
			say("Running: %s", strings.Join(next.Fix.Command, " "))
			c := exec.CommandContext(ctx, next.Fix.Command[0], next.Fix.Command[1:]...)
			c.Stdin, c.Stdout, c.Stderr = os.Stdin, os.Stdout, os.Stderr
			if err := c.Run(); err != nil {
				return fmt.Errorf("%s: %w", next.Fix.Command[0], err)
			}
			if next.Fix.RecheckAfter {
				return errors.New("finish Apple's installer, then run the build again")
			}
		} else {
			last := ""
			tty := isTerminal()
			err := next.Fix.Run(ctx, r, func(msg string, done, total int64) {
				if done == deps.ToolOutput && !tty {
					return
				}
				if !tty && total > 0 {
					return // no byte counters in logs
				}
				line := msg
				if total > 0 {
					line = fmt.Sprintf("%s  %s / %s", msg, download.HumanBytes(done), download.HumanBytes(total))
				}
				if line != last {
					if tty {
						fmt.Printf("\r\033[K    %s", line)
					} else {
						fmt.Printf("    %s\n", line)
					}
					last = line
				}
			})
			fmt.Println()
			if err != nil {
				return err
			}
		}
		r = deps.Check(ctx, info, src, data)
	}

	inst := paths.Locate(info, name, f.prefix, cfg.InstallDir)
	if f.prefix == "" && cfg.InstallDir != "" && paths.Occupied(inst) {
		return fmt.Errorf("%s already holds something that isn't a Patchsu build; choose another install folder", inst.Root())
	}
	if err := os.MkdirAll(data.Logs(), 0o755); err != nil {
		return err
	}
	logPath := filepath.Join(data.Logs(), "build-"+time.Now().Format("20060102-150405")+".log")
	logFile, err := os.Create(logPath)
	if err != nil {
		return err
	}
	defer logFile.Close()
	tools := r.Tools
	b := &build.Builder{Info: info, Source: src, Tools: &tools, Install: inst, Selection: sel, Preset: preset,
		CLIVersion: Version, Launcher: !f.noLauncher, Log: logFile}
	steps := b.Steps()
	events := make(chan build.Event, 256)
	done := make(chan error, 1)
	start := time.Now()
	go func() { done <- b.Run(ctx, events) }()
	announced := -1
	for ev := range events {
		switch {
		case ev.Line != "":
			continue
		case ev.State == build.Running && ev.Step != announced:
			announced = ev.Step
			say("%s", steps[ev.Step].Name)
		case ev.State == build.Failed:
			fmt.Println("    failed:", ev.Detail)
		}
	}
	if err := <-done; err != nil {
		return fmt.Errorf("%w\nFull log: %s", err, logPath)
	}
	say("Done in %s.", time.Since(start).Round(time.Second))
	if inst.AppBundle != "" {
		fmt.Println("  App: ", inst.AppBundle)
	} else {
		fmt.Println("  Game:", inst.Executable)
	}
	fmt.Println("  Log: ", logPath)
	return nil
}

func isTerminal() bool {
	fi, err := os.Stdout.Stat()
	return err == nil && fi.Mode()&os.ModeCharDevice != 0
}

func confirm(q string) bool {
	fmt.Printf("%s [Y/n] ", q)
	var ans string
	fmt.Scanln(&ans)
	ans = strings.ToLower(strings.TrimSpace(ans))
	return ans == "" || ans == "y" || ans == "yes"
}

func cmdUninstall(f *flags) error {
	info, cfg := sysinfo.Detect(), config.Load()
	name := manifest.BaseName
	if f.server != "" {
		if !manifest.ValidServerName(f.server) {
			return fmt.Errorf("%q is not a server name", f.server)
		}
		name = (&manifest.Server{Name: f.server}).BuildName()
	}
	inst := paths.Locate(info, name, f.prefix, cfg.InstallDir)
	removed, err := build.Uninstall(info, inst)
	for _, p := range removed {
		say("Removed %s", p)
	}
	if err != nil {
		return err
	}
	if len(removed) == 0 {
		say("Nothing installed at %s", inst.Prefix)
	}
	say("Your osu! data (beatmaps, scores, local profiles) is untouched.")
	return nil
}

// cmdMakeRelease writes the two release assets: manifest.json (from patches.meta.json + hashes) and patches.zip.
func cmdMakeRelease(f *flags) error {
	if f.local == "" || f.tag == "" || f.out == "" {
		return errors.New("make-release needs --local DIR --tag TAG --out DIR")
	}
	m, err := release.BuildManifest(f.local, f.tag)
	if err != nil {
		return err
	}
	if err := os.MkdirAll(f.out, 0o755); err != nil {
		return err
	}
	data, err := m.Marshal()
	if err != nil {
		return err
	}
	if err := os.WriteFile(filepath.Join(f.out, release.ManifestAsset), append(data, '\n'), 0o644); err != nil {
		return err
	}
	zf, err := os.Create(filepath.Join(f.out, release.PatchesAsset))
	if err != nil {
		return err
	}
	defer zf.Close()
	zw := zip.NewWriter(zf)
	for _, p := range m.Patches {
		w, err := zw.Create(p.File)
		if err != nil {
			return err
		}
		in, err := os.Open(filepath.Join(f.local, p.File))
		if err != nil {
			return err
		}
		_, err = io.Copy(w, in)
		in.Close()
		if err != nil {
			return err
		}
	}
	if err := zw.Close(); err != nil {
		return err
	}
	say("Wrote %s and %s (%d patches, release %s)", filepath.Join(f.out, release.ManifestAsset),
		filepath.Join(f.out, release.PatchesAsset), len(m.Patches), f.tag)
	return nil
}
