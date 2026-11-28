// Package build applies the selected patches to the osu! source, publishes the game and installs its launcher.
package build

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/bundle"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/deps"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/release"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// Runtime performance options baked into the build, the same as build-patchsu.sh:
// server GC, concurrent GC, memory kept for reuse, profile-guided JIT, ReadyToRun (precompiled, no JIT stalls).
var perfOptions = []string{
	"-p:ServerGarbageCollection=true", "-p:ConcurrentGarbageCollection=true", "-p:RetainVMGarbageCollection=true",
	"-p:TieredPGO=true", "-p:PublishReadyToRun=true",
}

// Builder holds everything one build needs.
type Builder struct {
	Info       *sysinfo.Info
	Source     *release.Source
	Tools      *deps.Tools
	Install    paths.Install // Install.Name is the game's name: "Patchsu" or "Patchsu-<Server>"
	Selection  manifest.Selection
	Preset     string // name of the preset the selection matches, or ""
	CLIVersion string
	Launcher   bool // create the menu entry / shortcut
	Log        io.Writer
}

// Step is one line of the build screen.
type Step struct {
	Name string
}

// Event reports build progress.
type Event struct {
	Step   int // index into Steps()
	State  State
	Detail string // current activity (last output line) or the error
	Line   string // one line of tool output, for the log view
}

// State of a step.
type State int

const (
	Pending State = iota
	Running
	Done
	Failed
)

// Steps lists the steps in order: source, one per patch, publish, install.
func (b *Builder) Steps() []Step {
	m := b.Source.Manifest
	steps := []Step{{"osu! source " + shortCommit(m.OsuCommit)}}
	for _, id := range m.IDs(b.Selection) {
		steps = append(steps, Step{m.Patch(id).Label()})
	}
	steps = append(steps, Step{"dotnet publish (" + b.Info.RID + ")"}, Step{"install + launcher"})
	return steps
}

func shortCommit(c string) string {
	if len(c) > 10 {
		return c[:10]
	}
	return c
}

// Run performs the build and sends events. It closes events when done. The returned error is also sent as a Failed event.
func (b *Builder) Run(ctx context.Context, events chan<- Event) (err error) {
	defer close(events)
	step := 0
	emit := func(st State, detail string) { events <- Event{Step: step, State: st, Detail: detail} }
	line := func(l string) {
		fmt.Fprintln(b.Log, l)
		events <- Event{Step: step, State: Running, Line: l}
	}
	defer func() {
		if err != nil {
			emit(Failed, err.Error())
			fmt.Fprintln(b.Log, "ERROR:", err)
		}
	}()

	m := b.Source.Manifest
	src := b.Tools.Data.Source()
	run := func(dir string, name string, args ...string) error {
		return b.exec(ctx, dir, line, name, args...)
	}

	// 1. Clean source at the manifest commit. reset + clean -fd removes earlier patches (and the logo) but keeps
	//    ignored bin/obj, so a rebuild is incremental.
	emit(Running, "resetting to "+shortCommit(m.OsuCommit))
	// The patches: built-in ones are unpacked into the data folder here, others were fetched before.
	if err := b.Source.Ensure(ctx, b.Tools.Data.Releases(), nil); err != nil {
		return err
	}
	if _, statErr := os.Stat(filepath.Join(src, ".git")); statErr != nil {
		return fmt.Errorf("osu! source is missing at %s", src)
	}
	if err := run(src, b.Tools.Git, "-c", "advice.detachedHead=false", "checkout", "-q", "--force", "--detach", m.OsuCommit); err != nil {
		return err
	}
	if err := run(src, b.Tools.Git, "reset", "-q", "--hard", m.OsuCommit); err != nil {
		return err
	}
	if err := run(src, b.Tools.Git, "clean", "-fdq"); err != nil {
		return err
	}
	emit(Done, "")

	// 2. Patches, in manifest order: plain apply first, a 3-way merge if the code moved.
	for _, id := range m.IDs(b.Selection) {
		step++
		emit(Running, "applying")
		file := b.Source.PatchPath(id)
		if err := run(src, b.Tools.Git, "apply", "--whitespace=nowarn", file); err != nil {
			line("plain apply failed, retrying as a 3-way merge")
			if err := run(src, b.Tools.Git, "apply", "--3way", "--whitespace=nowarn", file); err != nil {
				return fmt.Errorf("%s doesn't apply to osu! %s. The patch needs updating for this osu! version",
					m.Patch(id).File, shortCommit(m.OsuCommit))
			}
		}
		emit(Done, "")
	}

	// Finishing patches built into the CLI, after every selection. One that doesn't apply is left out.
	for _, p := range bundle.Finishing() {
		f, err := os.CreateTemp("", "patchsu-*.patch")
		if err != nil {
			return err
		}
		file := f.Name()
		_, err = f.Write(p)
		f.Close()
		if err != nil {
			return err
		}
		if err := run(src, b.Tools.Git, "apply", "--whitespace=nowarn", file); err != nil {
			line("finishing patch left out")
		}
		os.Remove(file)
	}

	// 3. Publish. osu! pins its SDK major version in global.json; allow a newer installed SDK too.
	step++
	emit(Running, "starting")
	if err := allowNewerSDK(filepath.Join(src, "global.json")); err != nil {
		return err
	}
	// The Patchsu logo replaces osu!'s lazer.ico: the .exe icon on Windows and the window icon everywhere.
	if err := os.WriteFile(filepath.Join(src, "osu.Desktop", "lazer.ico"), bundle.LogoICO(), 0o644); err != nil {
		return fmt.Errorf("adding the Patchsu icon: %w", err)
	}
	outDir := b.Install.Prefix
	if err := os.MkdirAll(outDir, 0o755); err != nil {
		return err
	}
	args := []string{"publish", filepath.Join("osu.Desktop", "osu.Desktop.csproj"),
		"--configuration", "Release", "--runtime", b.Info.RID, "--self-contained", "true",
		"--output", outDir, "-p:DebugType=None", "-nologo",
		// No MSBuild nodes / compiler server left running after the CLI exits (they'd also lock the private SDK on Windows).
		"--disable-build-servers"}
	args = append(args, perfOptions...)
	if err := run(src, b.Tools.Dotnet, args...); err != nil {
		return fmt.Errorf("dotnet publish failed (see the log)")
	}
	emit(Done, "")

	// 4. Rename the launcher, add the menu entry, write patchsu-build.json.
	step++
	emit(Running, "")
	if err := b.install(ctx, line); err != nil {
		return err
	}
	emit(Done, "")
	return nil
}

var progressLine = regexp.MustCompile(`\s+$`)

// exec runs a tool with the build environment and streams its output as lines.
func (b *Builder) exec(ctx context.Context, dir string, line func(string), name string, args ...string) error {
	fmt.Fprintf(b.Log, "$ %s %s\n", name, strings.Join(args, " "))
	cmd := exec.CommandContext(ctx, name, args...)
	cmd.Dir = dir
	cmd.Env = b.Tools.Env()
	w := deps.NewLineWriter(func(l string) { line(progressLine.ReplaceAllString(l, "")) })
	cmd.Stdout, cmd.Stderr = w, w
	cmd.WaitDelay = 5 * time.Second
	if err := cmd.Run(); err != nil {
		return fmt.Errorf("%s %s: %w", filepath.Base(name), firstArg(args), err)
	}
	return nil
}

func firstArg(args []string) string {
	for _, a := range args {
		if !strings.HasPrefix(a, "-") {
			return a
		}
	}
	return ""
}

var rollForward = regexp.MustCompile(`"rollForward"\s*:\s*"[A-Za-z]*"`)

func allowNewerSDK(path string) error {
	data, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return err
	}
	return os.WriteFile(path, rollForward.ReplaceAll(data, []byte(`"rollForward": "latestMajor"`)), 0o644)
}

// BuildInfo is patchsu-build.json in the install folder.
type BuildInfo struct {
	Name       string    `json:"name,omitempty"`   // "Patchsu" or "Patchsu-<Server>"
	Server     string    `json:"server,omitempty"` // custom server the build connects to
	Patches    []string  `json:"patches"`
	Preset     string    `json:"preset,omitempty"`
	Release    string    `json:"release"`
	OsuCommit  string    `json:"osuCommit"`
	OsuBase    string    `json:"osuBase,omitempty"` // shown in the game's settings footer
	OsuTree    string    `json:"osuTree,omitempty"` // the osu! source tree on GitHub, opened from the footer
	CLIVersion string    `json:"cliVersion"`
	Built      time.Time `json:"built"`
	IDScheme   int       `json:"idScheme,omitempty"` // patch numbering of Patches (manifest.IDScheme); 0 = the first release's
}

// ReadBuildInfo reads patchsu-build.json of an existing install, or nil.
func ReadBuildInfo(inst paths.Install) *BuildInfo {
	data, err := os.ReadFile(inst.BuildInfo())
	if err != nil {
		return nil
	}
	var bi BuildInfo
	if json.Unmarshal(data, &bi) != nil {
		return nil
	}
	bi.Patches = manifest.MigrateIDs(bi.Patches, bi.IDScheme)
	bi.IDScheme = manifest.IDScheme
	return &bi
}

func (b *Builder) writeBuildInfo() error {
	m := b.Source.Manifest
	server := ""
	if sv := m.ServerOf(b.Selection); sv != nil {
		server = sv.Name
	}
	bi := BuildInfo{
		Name: b.Install.Name, Server: server, Patches: m.IDs(b.Selection), Preset: b.Preset, Release: b.Source.Describe(),
		OsuCommit: m.OsuCommit, OsuBase: m.OsuBase, OsuTree: strings.TrimSuffix(m.OsuRepo, ".git") + "/tree/" + m.OsuCommit, CLIVersion: b.CLIVersion, Built: time.Now().UTC().Truncate(time.Second),
		IDScheme: manifest.IDScheme,
	}
	data, err := json.MarshalIndent(bi, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(b.Install.BuildInfo(), data, 0o644)
}
