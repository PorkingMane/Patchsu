// Package deps checks what a build needs (.NET SDK, git, osu! source, patches, disk space) and fetches what's missing.
package deps

import (
	"bytes"
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/release"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// Tools are the programs found for the build.
type Tools struct {
	Dotnet        string // path of the dotnet executable
	DotnetVersion string
	Git           string
	GitVersion    string
	Data          paths.Data
}

// Env returns the environment for dotnet and git processes: the found SDK first on PATH, a private NuGet cache,
// and no telemetry or interactive prompts.
func (t *Tools) Env() []string {
	env := os.Environ()
	set := func(k, v string) {
		prefix := k + "="
		for i, e := range env {
			if strings.HasPrefix(strings.ToUpper(e), strings.ToUpper(prefix)) {
				env[i] = prefix + v
				return
			}
		}
		env = append(env, prefix+v)
	}
	path := os.Getenv("PATH")
	if t.Git != "" {
		path = filepath.Dir(t.Git) + string(os.PathListSeparator) + path
	}
	if t.Dotnet != "" {
		root := filepath.Dir(t.Dotnet)
		path = root + string(os.PathListSeparator) + path
		set("DOTNET_ROOT", root)
	}
	set("PATH", path)
	set("NUGET_PACKAGES", t.Data.NuGet())
	set("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
	set("DOTNET_NOLOGO", "1")
	set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
	set("GIT_TERMINAL_PROMPT", "0")
	return env
}

// Item is one line of the dependency check.
type Item struct {
	Key    string // "dotnet", "git", "source", "patches"
	Group  string // "Build" or "Getting the files"
	Name   string
	OK     bool
	Detail string
	Fix    *Fix
}

// Fix says how a missing item is fetched.
type Fix struct {
	// Summary is shown before asking, e.g. "~230 MB from dotnet.microsoft.com" or "sudo pacman -S git".
	Summary string
	// Command, when set, is run in the foreground with the terminal attached (it may ask for a password).
	Command []string
	// Run, when set, does the work in the background.
	Run func(ctx context.Context, r *Report, p Progress) error
	// Manual, when set, means the CLI can't do it: the text says what the user has to do.
	Manual string
	// RecheckAfter: the item is checked again after the fix instead of being assumed done (e.g. Apple's dialog).
	RecheckAfter bool
}

// Progress reports what a fix is doing. done is ToolOutput for raw output lines of a tool (e.g. git's progress).
type Progress func(msg string, done, total int64)

// ToolOutput marks a Progress message as a raw output line of a tool.
const ToolOutput = -2

// Report is the result of a check.
type Report struct {
	Info     *sysinfo.Info
	Manifest *manifest.Manifest
	Source   *release.Source
	Tools    Tools
	Items    []Item
	Free     int64
	Needed   int64
	Parts    []SizePart
}

// Missing returns the items that aren't ready.
func (r *Report) Missing() []Item {
	var out []Item
	for _, it := range r.Items {
		if !it.OK {
			out = append(out, it)
		}
	}
	return out
}

// Ready reports whether everything is there and there's enough disk space.
func (r *Report) Ready() bool { return len(r.Missing()) == 0 && r.DiskOK() }

// DiskOK reports whether there's enough free space (unknown counts as enough).
func (r *Report) DiskOK() bool { return r.Free < 0 || r.Free >= r.Needed }

// Check looks for everything the build needs.
func Check(ctx context.Context, info *sysinfo.Info, src *release.Source, data paths.Data) *Report {
	r := &Report{Info: info, Manifest: src.Manifest, Source: src, Tools: Tools{Data: data}}

	dn := r.checkDotnet()
	gi := r.checkGit()
	so := r.checkSource(ctx)
	pa := r.checkPatches()
	r.Items = []Item{dn, gi, so, pa}

	// Space estimate from what's already there (measured on a Linux x64 build of osu! 8cb6e6e68d):
	// install ~360 MB, .NET SDK ~630 MB, source + build output ~700 MB, NuGet packages ~1.6 GB.
	const mb = int64(1) << 20
	part := func(name string, n int64) {
		r.Needed += n
		r.Parts = append(r.Parts, SizePart{name, n})
	}
	part("game install", 400*mb)
	if !dn.OK {
		part(".NET SDK", 700*mb)
	}
	switch {
	case !so.OK:
		part("osu! source + build files", 800*mb)
	case !nonEmpty(filepath.Join(data.Source(), "osu.Game", "obj")):
		part("build files", 700*mb)
	default:
		part("rebuild", 100*mb)
	}
	if nonEmpty(data.NuGet()) {
		part("NuGet updates", 100*mb)
	} else {
		part("NuGet packages", 1700*mb)
	}
	r.Free = sysinfo.FreeSpace(data.Root)
	return r
}

// SizePart is one entry of the space estimate.
type SizePart struct {
	Name  string
	Bytes int64
}

func nonEmpty(dir string) bool {
	entries, err := os.ReadDir(dir)
	return err == nil && len(entries) > 0
}

// ---- .NET SDK ----

type sdk struct {
	path    string
	version string
	major   int
}

func listSDKs(dotnet string) []string {
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, dotnet, "--list-sdks")
	cmd.Env = append(os.Environ(), "DOTNET_ROOT="+filepath.Dir(dotnet), "DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_NOLOGO=1")
	out, err := cmd.Output()
	if err != nil {
		return nil
	}
	var vs []string
	for _, line := range strings.Split(string(out), "\n") {
		if f := strings.Fields(line); len(f) > 0 {
			vs = append(vs, f[0])
		}
	}
	return vs
}

func major(v string) int {
	n, _ := strconv.Atoi(strings.SplitN(v, ".", 2)[0])
	return n
}

func (r *Report) dotnetCandidates() []string {
	exe := r.Info.ExeName("dotnet")
	c := []string{filepath.Join(r.Tools.Data.Dotnet(), exe)}
	if p, err := exec.LookPath("dotnet"); err == nil {
		if resolved, err := filepath.EvalSymlinks(p); err == nil {
			p = resolved
		}
		c = append(c, p)
	}
	c = append(c, filepath.Join(r.Info.HomeDir, ".dotnet", exe))
	switch r.Info.OS {
	case "windows":
		c = append(c, filepath.Join(os.Getenv("ProgramFiles"), "dotnet", exe))
	case "darwin":
		c = append(c, "/usr/local/share/dotnet/dotnet", "/opt/homebrew/opt/dotnet/libexec/dotnet")
	default:
		c = append(c, "/usr/share/dotnet/dotnet", "/usr/lib/dotnet/dotnet")
	}
	return c
}

// checkDotnet prefers an SDK with the manifest's major version; a newer one works too (rollForward latestMajor).
func (r *Report) checkDotnet() Item {
	it := Item{Key: "dotnet", Group: "Build", Name: ".NET SDK " + r.Manifest.DotnetSDK}
	want := major(r.Manifest.DotnetSDK)
	var found []sdk
	seen := map[string]bool{}
	for _, c := range r.dotnetCandidates() {
		if seen[c] {
			continue
		}
		seen[c] = true
		if _, err := os.Stat(c); err != nil {
			continue
		}
		for _, v := range listSDKs(c) {
			if m := major(v); m >= want {
				found = append(found, sdk{c, v, m})
			}
		}
	}
	if len(found) > 0 {
		sort.SliceStable(found, func(i, j int) bool {
			ei, ej := found[i].major == want, found[j].major == want
			if ei != ej {
				return ei
			}
			return compareVersions(found[i].version, found[j].version) > 0
		})
		best := found[0]
		r.Tools.Dotnet, r.Tools.DotnetVersion = best.path, best.version
		it.OK = true
		it.Name = ".NET SDK " + best.version
		it.Detail = tildify(best.path, r.Info.HomeDir)
		if best.major != want {
			it.Detail += fmt.Sprintf("  (newer than %d, allowed)", want)
		}
		return it
	}
	it.Detail = "not found"
	it.Fix = &Fix{
		Summary: "~230 MB from dotnet.microsoft.com, into " + tildify(r.Tools.Data.Dotnet(), r.Info.HomeDir),
		Run: func(ctx context.Context, rep *Report, p Progress) error {
			return installDotnet(ctx, rep.Info, rep.Manifest.DotnetSDK, rep.Tools.Data, p)
		},
	}
	return it
}

func compareVersions(a, b string) int {
	pa, pb := strings.FieldsFunc(a, isSep), strings.FieldsFunc(b, isSep)
	for i := 0; i < len(pa) || i < len(pb); i++ {
		var x, y int
		if i < len(pa) {
			x, _ = strconv.Atoi(pa[i])
		}
		if i < len(pb) {
			y, _ = strconv.Atoi(pb[i])
		}
		if x != y {
			if x > y {
				return 1
			}
			return -1
		}
	}
	return 0
}

func isSep(r rune) bool { return r == '.' || r == '-' }

// ---- git ----

func gitVersion(git string) string {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	out, err := exec.CommandContext(ctx, git, "--version").Output()
	if err != nil {
		return ""
	}
	return strings.TrimPrefix(strings.TrimSpace(string(out)), "git version ")
}

func (r *Report) checkGit() Item {
	it := Item{Key: "git", Group: "Getting the files", Name: "git"}
	var candidates []string
	if r.Info.OS == "windows" {
		candidates = append(candidates, filepath.Join(r.Tools.Data.Git(), "cmd", "git.exe"))
	}
	if p, err := exec.LookPath("git"); err == nil {
		// On macOS /usr/bin/git is a stub that opens Apple's install dialog when the Command Line Tools are missing.
		if !(r.Info.OS == "darwin" && p == "/usr/bin/git" && exec.Command("xcode-select", "-p").Run() != nil) {
			candidates = append(candidates, p)
		}
	}
	for _, c := range candidates {
		if _, err := os.Stat(c); err != nil {
			continue
		}
		if v := gitVersion(c); v != "" {
			r.Tools.Git, r.Tools.GitVersion = c, v
			it.OK, it.Name, it.Detail = true, "git "+v, tildify(c, r.Info.HomeDir)
			return it
		}
	}
	it.Detail = "not found"
	switch r.Info.OS {
	case "windows":
		it.Fix = &Fix{
			Summary: "MinGit (~40 MB) from github.com/git-for-windows, into " + r.Tools.Data.Git(),
			Run: func(ctx context.Context, rep *Report, p Progress) error {
				return installMinGit(ctx, rep.Info, rep.Tools.Data, p)
			},
		}
	case "darwin":
		if r.Info.PkgMgr != nil {
			it.Fix = &Fix{Summary: "brew install git", Command: r.Info.PkgMgr.Install}
		} else {
			it.Fix = &Fix{
				Summary:      "xcode-select --install  (Apple's Command Line Tools installer, includes git)",
				Command:      []string{"xcode-select", "--install"},
				RecheckAfter: true,
			}
		}
	default:
		if r.Info.PkgMgr == nil {
			it.Fix = &Fix{Manual: "Install git with your system's package manager, then check again."}
			break
		}
		cmd, err := r.Info.ElevatedCommand(r.Info.PkgMgr)
		if err != nil {
			it.Fix = &Fix{Manual: err.Error() + ". Install git yourself, then check again."}
			break
		}
		it.Fix = &Fix{Summary: strings.Join(cmd, " "), Command: cmd}
	}
	return it
}

// ---- osu! source ----

func (r *Report) checkSource(ctx context.Context) Item {
	commit := r.Manifest.OsuCommit
	it := Item{Key: "source", Group: "Getting the files", Name: "osu!lazer source"}
	dir := r.Tools.Data.Source()
	if r.Tools.Git != "" {
		if _, err := os.Stat(filepath.Join(dir, ".git")); err == nil {
			cmd := exec.CommandContext(ctx, r.Tools.Git, "-C", dir, "cat-file", "-e", commit+"^{commit}")
			if cmd.Run() == nil {
				it.OK = true
				it.Detail = short(commit) + "  " + tildify(dir, r.Info.HomeDir)
				return it
			}
		}
	}
	it.Detail = "not downloaded yet (" + short(commit) + ")"
	it.Fix = &Fix{
		Summary: "~30 MB from " + strings.TrimPrefix(r.Manifest.OsuRepo, "https://"),
		Run: func(ctx context.Context, rep *Report, p Progress) error {
			return FetchSource(ctx, &rep.Tools, rep.Manifest.OsuRepo, commit, p)
		},
	}
	return it
}

func short(commit string) string {
	if len(commit) > 10 {
		return commit[:10]
	}
	return commit
}

// FetchSource makes data/osu-lazer contain the given commit: a shallow fetch of just that commit (fast, small).
func FetchSource(ctx context.Context, t *Tools, repo, commit string, p Progress) error {
	if t.Git == "" {
		return fmt.Errorf("git is needed to download the osu! source")
	}
	dir := t.Data.Source()
	git := func(args ...string) error {
		cmd := exec.CommandContext(ctx, t.Git, append([]string{"-C", dir}, args...)...)
		cmd.Env = t.Env()
		var stderr bytes.Buffer
		cmd.Stderr = &lineWriter{fn: func(l string) { p(l, ToolOutput, -1) }, also: &stderr}
		if err := cmd.Run(); err != nil {
			return fmt.Errorf("git %s: %v\n%s", args[0], err, strings.TrimSpace(stderr.String()))
		}
		return nil
	}
	if _, err := os.Stat(filepath.Join(dir, ".git")); err != nil {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return err
		}
		p("Creating "+dir, -1, -1)
		if err := git("init", "-q"); err != nil {
			return err
		}
		if err := git("remote", "add", "origin", repo); err != nil {
			return err
		}
	} else {
		_ = git("remote", "set-url", "origin", repo)
	}
	p("Downloading osu!lazer "+short(commit), -1, -1)
	if err := git("fetch", "--depth", "1", "--progress", "origin", commit); err != nil {
		return err
	}
	return git("-c", "advice.detachedHead=false", "checkout", "-q", "--force", "--detach", commit)
}

// lineWriter turns git's progress output (\r and \n separated) into lines.
type lineWriter struct {
	buf  []byte
	fn   func(string)
	also *bytes.Buffer
}

func (w *lineWriter) Write(b []byte) (int, error) {
	if w.also != nil {
		w.also.Write(b)
	}
	for _, c := range b {
		if c == '\n' || c == '\r' {
			if len(w.buf) > 0 {
				w.fn(string(w.buf))
				w.buf = w.buf[:0]
			}
			continue
		}
		w.buf = append(w.buf, c)
	}
	return len(b), nil
}

// NewLineWriter returns a writer that calls fn for every line (split on \n and \r).
func NewLineWriter(fn func(string)) *lineWriter { return &lineWriter{fn: fn} }

// ---- patches ----

func (r *Report) checkPatches() Item {
	n := len(r.Manifest.Patches)
	it := Item{Key: "patches", Group: "Getting the files", Name: "patches"}
	if !r.Source.NeedsDownload() {
		it.OK = true
		it.Detail = fmt.Sprintf("%s, %d patches", r.Source.Describe(), n)
		return it
	}
	it.Name = "patches " + r.Source.Tag
	it.Detail = "not downloaded yet"
	size := ""
	if s := r.Source.DownloadSize(); s > 0 {
		size = download.HumanBytes(s) + " "
	}
	it.Fix = &Fix{
		Summary: size + "from github.com/" + release.Repo,
		Run: func(ctx context.Context, rep *Report, p Progress) error {
			return rep.Source.Ensure(ctx, rep.Tools.Data.Releases(), func(d, t int64) { p("Downloading patches", d, t) })
		},
	}
	return it
}

func tildify(p, home string) string {
	if home != "" && runtime.GOOS != "windows" && strings.HasPrefix(p, home+string(os.PathSeparator)) {
		return "~" + p[len(home):]
	}
	return p
}

// Tildify shortens a path under the home folder to ~/...
func Tildify(p, home string) string { return tildify(p, home) }
