// Package tui is the interactive Patchsu-CLI: pick patches, check the system, fetch what's missing, build.
package tui

import (
	"context"
	"fmt"
	"io"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/spinner"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/build"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/config"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/deps"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/release"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// Options come from the command line.
type Options struct {
	Version    string
	Tag        string // --release
	PatchesDir string // --local (or the patches folder next to the program)
	Builtin    fs.FS  // patches built into the CLI, nil if none
	DataDir    string // --data-dir
	Prefix     string // --prefix
	NoLauncher bool   // --no-launcher
	NoUpdate   bool   // --no-update: don't look for a newer patch release than the built-in one
}

type screen int

const (
	scrLoading screen = iota
	scrPicker
	scrDataDir
	scrChecking
	scrCheck
	scrFixing
	scrBuild
	scrDone
	scrError
	scrInstallDir
)

type model struct {
	opts    Options
	info    *sysinfo.Info
	cfg     *config.Config
	install paths.Install
	prog    *tea.Program

	scr           screen
	width, height int
	spin          spinner.Model
	bar           progress.Model
	err           error
	exitMessage   string

	// release + selection
	src        *release.Source
	m          *manifest.Manifest
	sel        manifest.Selection
	cursor     int // 0 = preset row, then the server rows (server presets only), then the patches
	presetIdx  int // index into m.Presets, len(m.Presets) = Custom
	fromPreset string
	notice     string
	serverIdx  int                         // server chosen for server presets, index into m.Servers
	installed  map[string]*build.BuildInfo // patchsu-build.json of each build name where it installs (nil: none)

	// data folder
	dataDir   string
	dirOpts   []dirOption
	dirCursor int
	dirInput  field
	dirErr    string
	dirBack   screen // where esc goes: the picker (first run) or the check screen

	// install location
	instOpts   []instOption
	instCursor int
	instInput  field
	instErr    string

	// check + fixes
	report      *deps.Report
	bgChecking  bool
	checkCursor int
	attempted   map[string]bool
	fixKey      string
	fixMsg      string
	fixDone     int64
	fixTotal    int64
	fixErr      error
	waitRecheck string // message shown after a fix that needs the user (Apple's installer)

	// build
	builder    *build.Builder
	steps      []build.Step
	states     []build.State
	details    []string
	logLines   []string
	showLog    bool
	logPath    string
	buildStart time.Time
	buildEnd   time.Time
	buildErr   error
	cancel     context.CancelFunc
	quitArmed  bool
	cleaned    string
}

type dirOption struct {
	label string
	path  string
	note  string
	typed bool // "Somewhere else": the folder comes from the text field
}

// Run starts the interactive CLI.
func Run(opts Options) error {
	info := sysinfo.Detect()
	cfg := config.Load()
	m := &model{
		opts:      opts,
		info:      info,
		cfg:       cfg,
		install:   paths.Locate(info, "", opts.Prefix, cfg.InstallDir),
		installed: map[string]*build.BuildInfo{},
		spin:      spinner.New(spinner.WithSpinner(spinner.MiniDot), spinner.WithStyle(sAccent)),
		bar:       progress.New(progress.WithGradient("#FF66AA", "#B388FF"), progress.WithWidth(30), progress.WithoutPercentage()),
		attempted: map[string]bool{},
	}
	m.dataDir = opts.DataDir
	if m.dataDir == "" {
		m.dataDir = cfg.DataDir
	}

	p := tea.NewProgram(m, tea.WithAltScreen())
	m.prog = p
	final, err := p.Run()
	if err != nil {
		return err
	}
	if fm, ok := final.(*model); ok {
		if fm.cancel != nil {
			fm.cancel()
		}
		if fm.exitMessage != "" {
			fmt.Println(fm.exitMessage)
		}
		if fm.err != nil && fm.scr == scrError {
			return fm.err
		}
	}
	return nil
}

// ---- messages ----

type releaseMsg struct {
	src    *release.Source
	notice string // about a patch update from GitHub
	err    error
}
type reportMsg struct{ r *deps.Report }

// bgReportMsg is the result of the silent check that runs while patches are picked.
type bgReportMsg struct{ r *deps.Report }
type fixProgressMsg struct {
	msg         string
	done, total int64
}
type fixDoneMsg struct {
	key string
	err error
}
type buildEventMsg struct{ ev build.Event }
type buildDoneMsg struct{ err error }
type cleanedMsg struct {
	msg string
	err error
}

func (m *model) Init() tea.Cmd {
	return tea.Batch(m.spin.Tick, m.loadRelease())
}

func (m *model) data() paths.Data { return paths.Data{Root: m.dataDir} }

func (m *model) loadRelease() tea.Cmd {
	opts, cache := m.opts, ""
	if m.dataDir != "" {
		cache = m.data().Releases()
	}
	return func() tea.Msg {
		ctx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
		defer cancel()
		s, err := release.Open(ctx, opts.PatchesDir, opts.Tag, opts.Builtin, cache)
		notice := ""
		if err == nil && !opts.NoUpdate && s.Origin == "builtin" {
			uctx, ucancel := context.WithTimeout(context.Background(), release.UpdateTimeout)
			up, n := release.Update(uctx, s, cache, opts.Version)
			ucancel()
			if up != nil {
				s = up
			}
			notice = n
		}
		return releaseMsg{s, notice, err}
	}
}

// probeDir is the data folder used for the silent check: the chosen one, else where Documents would put it.
func (m *model) probeDir() string {
	if m.dataDir != "" {
		return m.dataDir
	}
	return filepath.Join(m.info.DocumentsDir(), paths.DataDirName)
}

// backgroundCheck checks the system while the user picks patches, without changing the screen.
func (m *model) backgroundCheck() tea.Cmd {
	m.bgChecking = true
	info, src, data := m.info, m.src, paths.Data{Root: m.probeDir()}
	return func() tea.Msg { return bgReportMsg{deps.Check(context.Background(), info, src, data)} }
}

// toCheck goes to the check screen, reusing the silent check's result when it was for the chosen data folder.
func (m *model) toCheck() tea.Cmd {
	if m.report != nil && !m.bgChecking && m.report.Tools.Data.Root == m.dataDir {
		m.checkCursor = 0
		m.scr = scrCheck
		return nil
	}
	return m.runCheck()
}

func (m *model) runCheck() tea.Cmd {
	m.scr = scrChecking
	info, src, data := m.info, m.src, m.data()
	return tea.Batch(m.spin.Tick, func() tea.Msg {
		return reportMsg{deps.Check(context.Background(), info, src, data)}
	})
}

// ---- update ----

func (m *model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.WindowSizeMsg:
		m.width, m.height = msg.Width, msg.Height
		m.bar.Width = min(40, max(10, msg.Width-50))
		return m, nil

	case spinner.TickMsg:
		var cmd tea.Cmd
		m.spin, cmd = m.spin.Update(msg)
		return m, cmd

	case releaseMsg:
		if msg.err == nil {
			msg.err = msg.src.Manifest.CheckCLI(m.opts.Version)
		}
		if msg.err != nil {
			m.err, m.scr = msg.err, scrError
			return m, nil
		}
		m.src, m.m = msg.src, msg.src.Manifest
		m.initSelection()
		m.notice = msg.notice
		m.scr = scrPicker
		return m, m.backgroundCheck()

	case bgReportMsg:
		m.bgChecking = false
		if m.scr == scrChecking || m.scr == scrFixing {
			return m, nil // a foreground check is running; its result wins
		}
		m.report = msg.r
		return m, nil

	case reportMsg:
		m.report = msg.r
		m.checkCursor = 0
		if m.scr == scrFixing {
			return m, m.nextFix()
		}
		m.scr = scrCheck
		return m, nil

	case fixProgressMsg:
		m.fixMsg, m.fixDone, m.fixTotal = msg.msg, msg.done, msg.total
		return m, nil

	case fixDoneMsg:
		if msg.err != nil {
			m.fixErr = msg.err
			m.scr = scrCheck
			return m, nil
		}
		m.fixMsg, m.fixDone, m.fixTotal = "Checking again", 0, 0
		if it := m.findItem(msg.key); it != nil && it.Fix != nil && it.Fix.RecheckAfter {
			m.waitRecheck = "Finish Apple's installer, then press r to check again."
			m.scr = scrCheck
			return m, nil
		}
		info, src, data := m.info, m.src, m.data()
		return m, func() tea.Msg { return reportMsg{deps.Check(context.Background(), info, src, data)} }

	case buildEventMsg:
		m.applyEvent(msg.ev)
		return m, nil

	case buildDoneMsg:
		m.buildEnd = time.Now()
		m.buildErr = msg.err
		m.cancel = nil
		if msg.err == nil {
			m.scr = scrDone
		}
		return m, nil

	case cleanedMsg:
		if msg.err != nil {
			m.cleaned = "Couldn't delete everything: " + msg.err.Error()
		} else {
			m.cleaned = msg.msg
		}
		return m, nil

	case tea.KeyMsg:
		return m.key(msg)
	}
	return m, nil
}

func (m *model) key(k tea.KeyMsg) (tea.Model, tea.Cmd) {
	s := k.String()
	if s == "ctrl+c" {
		if m.scr == scrBuild && m.buildErr == nil && m.cancel != nil {
			if !m.quitArmed {
				m.quitArmed = true
				return m, nil
			}
			m.cancel()
		}
		return m, tea.Quit
	}
	switch m.scr {
	case scrInstallDir:
		return m.installDirKey(k)
	case scrPicker:
		return m.pickerKey(s)
	case scrDataDir:
		return m.dirKey(k)
	case scrCheck:
		return m.checkKey(s)
	case scrBuild:
		switch s {
		case "l":
			m.showLog = !m.showLog
		case "q", "esc":
			if m.buildErr != nil {
				return m, tea.Quit
			}
			m.quitArmed = true
		}
		return m, nil
	case scrDone:
		return m.doneKey(s)
	case scrError, scrLoading:
		if s == "q" || s == "esc" || s == "enter" {
			return m, tea.Quit
		}
	}
	return m, nil
}

// ---- data folder ----

// openDataDir asks where the data folder goes: next to the program, Documents, next to osu!'s data, or a typed folder.
// back is the screen esc returns to.
func (m *model) openDataDir(back screen) {
	osu := paths.FindOsuData(m.info)
	m.dirOpts = []dirOption{
		{label: "Next to this program", path: filepath.Join(m.info.Executable, paths.DataDirName)},
		{label: "Documents", path: filepath.Join(m.info.DocumentsDir(), paths.DataDirName)},
		{label: "Next to osu! data", path: filepath.Join(osu.Parent, paths.DataDirName), note: m.osuNote(osu)},
		{label: "Somewhere else", typed: true},
	}
	m.dirInput = field{placeholder: "type a folder, e.g. " + filepath.Join(m.info.HomeDir, "Games")}
	m.dirCursor, m.dirErr, m.dirBack = -1, "", back
	for i, o := range m.dirOpts {
		if !o.typed && o.path == m.dataDir {
			m.dirCursor = i
		}
	}
	if m.dirCursor < 0 && m.dataDir != "" {
		m.dirCursor = len(m.dirOpts) - 1
		m.dirInput.value = []rune(m.dataDir)
	}
	if m.dirCursor < 0 {
		m.dirCursor = 0
		for m.dirCursor < 2 && m.dirProblem(m.dirCursor) != "" {
			m.dirCursor++
		}
	}
	m.dirInput.focused = m.dirOpts[m.dirCursor].typed
	m.scr = scrDataDir
}

// osuNote says where osu!'s data folder is, for the "Next to osu! data" rows.
func (m *model) osuNote(osu paths.OsuData) string {
	if osu.Found {
		return "osu! data: " + deps.Tildify(osu.Dir, m.info.HomeDir)
	}
	return "osu! data would be in " + deps.Tildify(osu.Dir, m.info.HomeDir) + " (not there yet)"
}

// dirPath is the data folder option i stands for. A typed folder gets a patchsu-data folder inside it (unless it is
// one), because "delete the data folder" after a build removes the whole folder.
func (m *model) dirPath(i int) (string, error) {
	o := m.dirOpts[i]
	if !o.typed {
		return o.path, nil
	}
	p := expandPath(string(m.dirInput.value), m.info.HomeDir)
	if p == "" {
		return "", fmt.Errorf("type a folder first")
	}
	if !filepath.IsAbs(p) {
		return "", fmt.Errorf("type a full path")
	}
	if filepath.Base(p) != paths.DataDirName {
		p = filepath.Join(p, paths.DataDirName)
	}
	return p, nil
}

func (m *model) dirProblem(i int) string {
	p, err := m.dirPath(i)
	if err != nil {
		return err.Error()
	}
	if !sysinfo.Writable(p) {
		return "not writable"
	}
	return ""
}

func (m *model) dirKey(k tea.KeyMsg) (tea.Model, tea.Cmd) {
	s := k.String()
	n := len(m.dirOpts)
	switch s {
	case "esc":
		m.scr = m.dirBack
		return m, nil
	case "up", "shift+tab":
		m.dirCursor = (m.dirCursor + n - 1) % n
	case "down", "tab":
		m.dirCursor = (m.dirCursor + 1) % n
	case "enter":
		if p := m.dirProblem(m.dirCursor); p != "" {
			m.dirErr = p
			return m, nil
		}
		p, _ := m.dirPath(m.dirCursor)
		m.dataDir = p
		m.cfg.DataDir = p
		_ = m.cfg.Save()
		return m, m.toCheck()
	default:
		if m.dirOpts[m.dirCursor].typed {
			if m.dirInput.key(k) {
				m.dirErr = ""
			}
			return m, nil
		}
		switch s {
		case "q":
			return m, tea.Quit
		case "k":
			return m.dirKey(tea.KeyMsg{Type: tea.KeyUp})
		case "j":
			return m.dirKey(tea.KeyMsg{Type: tea.KeyDown})
		}
		return m, nil
	}
	m.dirErr = ""
	m.dirInput.focused = m.dirOpts[m.dirCursor].typed
	return m, nil
}

func (m *model) viewDataDir() string {
	var b strings.Builder
	home := m.info.HomeDir
	b.WriteString(sBold.Render("Where should Patchsu-CLI keep its downloads and build files?") + "\n\n")
	for i, o := range m.dirOpts {
		cur := "  "
		if i == m.dirCursor {
			cur = sCursor.Render("› ")
		}
		label := fmt.Sprintf("%-22s ", o.label)
		if o.typed {
			if i == m.dirCursor {
				label = sBold.Render(label)
			}
			b.WriteString(cur + label + m.dirInput.View() + "\n")
			continue
		}
		line := label + deps.Tildify(o.path, home)
		switch {
		case m.dirProblem(i) != "":
			line = sDim.Render(line + "  (" + m.dirProblem(i) + ")")
		case i == m.dirCursor:
			line = sBold.Render(line)
		}
		if o.note != "" {
			line += "\n" + strings.Repeat(" ", 25) + sDim.Render(o.note)
		}
		b.WriteString(cur + line + "\n")
	}
	b.WriteString("\n")
	if m.dirOpts[m.dirCursor].typed {
		if p, err := m.dirPath(m.dirCursor); err == nil {
			b.WriteString(sDim.Render("Uses "+deps.Tildify(p, home)) + "\n")
		} else {
			b.WriteString("\n")
		}
	} else {
		b.WriteString("\n")
	}
	if m.dirErr != "" {
		b.WriteString(sWarn.Render("Can't use that folder: "+m.dirErr) + "\n")
	} else {
		b.WriteString("\n")
	}
	b.WriteString(sDim.Render("About 4 GB. You can delete it after building; the game stays installed.") + "\n")
	b.WriteString(sDim.Render("Remembered in "+deps.Tildify(config.Path(), home)+". Override with --data-dir.") + "\n\n")
	b.WriteString(sKeys.Render("↑/↓ move  type a folder on the last row  enter choose  esc back"))
	return b.String()
}

// ---- view ----

func (m *model) View() string {
	var body string
	switch m.scr {
	case scrLoading:
		if m.opts.PatchesDir != "" || (m.opts.Tag == "" && m.opts.Builtin != nil && m.opts.NoUpdate) {
			body = m.spin.View() + " Reading the patches..."
		} else if m.opts.Tag == "" && m.opts.Builtin != nil {
			body = m.spin.View() + " Checking github.com/" + release.Repo + " for patch updates..."
		} else {
			body = m.spin.View() + " Looking for the patch release on github.com/" + release.Repo + "..."
		}
	case scrError:
		body = sBad.Render("Error: ") + wrap(m.err.Error(), m.width-8) + "\n\n" + sKeys.Render("q quit")
	case scrPicker:
		body = m.viewPicker()
	case scrDataDir:
		body = m.viewDataDir()
	case scrInstallDir:
		body = m.viewInstallDir()
	case scrChecking:
		body = m.spin.View() + " Checking what's needed..."
	case scrCheck:
		body = m.viewCheck()
	case scrFixing:
		body = m.viewFixing()
	case scrBuild:
		body = m.viewBuild()
	case scrDone:
		body = m.viewDone()
	}
	return lipgloss.NewStyle().Padding(1, 2).Render(m.header() + "\n\n" + body)
}

func (m *model) header() string {
	left := sTitle.Render("Patchsu-CLI") + sDim.Render(" "+m.opts.Version)
	right := ""
	if m.src != nil {
		right = sHeader.Render(m.src.Describe() + " · osu!lazer " + short(m.m.OsuCommit))
	}
	gap := m.width - 4 - lipgloss.Width(left) - lipgloss.Width(right)
	if gap < 2 {
		return left + "\n" + right
	}
	return left + strings.Repeat(" ", gap) + right
}

func short(c string) string {
	if len(c) > 10 {
		return c[:10]
	}
	return c
}

func wrap(s string, width int) string {
	if width < 20 {
		width = 72
	}
	return lipgloss.NewStyle().Width(width).Render(s)
}

// ---- fixes ----

func (m *model) findItem(key string) *deps.Item {
	if m.report == nil {
		return nil
	}
	for i := range m.report.Items {
		if m.report.Items[i].Key == key {
			return &m.report.Items[i]
		}
	}
	return nil
}

// fixOrder: git first, because the osu! source needs it.
var fixOrder = []string{"git", "dotnet", "source", "patches"}

// nextFix runs the next missing item's fix, or returns to the check screen when nothing is left.
func (m *model) nextFix() tea.Cmd {
	for _, key := range fixOrder {
		it := m.findItem(key)
		if it == nil || it.OK || it.Fix == nil || it.Fix.Manual != "" {
			continue
		}
		if m.attempted[key] {
			m.fixErr = fmt.Errorf("%s is still missing after trying to get it", it.Name)
			m.scr = scrCheck
			return nil
		}
		if key == "source" && m.report.Tools.Git == "" {
			continue // needs git first
		}
		m.attempted[key] = true
		m.fixKey, m.fixMsg, m.fixDone, m.fixTotal = key, "Starting", 0, 0
		m.scr = scrFixing
		if it.Fix.Command != nil {
			return tea.Exec(&announcedCmd{args: it.Fix.Command, title: it.Name}, func(err error) tea.Msg {
				return fixDoneMsg{key: key, err: err}
			})
		}
		run, report, prog := it.Fix.Run, m.report, m.prog
		return tea.Batch(m.spin.Tick, func() tea.Msg {
			err := run(context.Background(), report, func(msg string, done, total int64) {
				if done == deps.ToolOutput {
					done = 0
				}
				prog.Send(fixProgressMsg{msg, done, total})
			})
			return fixDoneMsg{key: key, err: err}
		})
	}
	m.scr = scrCheck
	return nil
}

// announcedCmd runs a command with the terminal attached and says first what it is and why.
type announcedCmd struct {
	args           []string
	title          string
	stdin          io.Reader
	stdout, stderr io.Writer
}

func (c *announcedCmd) SetStdin(r io.Reader)  { c.stdin = r }
func (c *announcedCmd) SetStdout(w io.Writer) { c.stdout = w }
func (c *announcedCmd) SetStderr(w io.Writer) { c.stderr = w }
func (c *announcedCmd) Run() error {
	fmt.Fprintf(c.stdout, "\n\033[1;35m==>\033[0m Installing %s:\n    %s\n", c.title, strings.Join(c.args, " "))
	if c.args[0] == "sudo" || c.args[0] == "doas" {
		fmt.Fprintln(c.stdout, "    Your password goes to "+c.args[0]+", not to Patchsu-CLI.")
	}
	fmt.Fprintln(c.stdout)
	cmd := exec.Command(c.args[0], c.args[1:]...)
	cmd.Stdin, cmd.Stdout, cmd.Stderr = c.stdin, c.stdout, c.stderr
	err := cmd.Run()
	if err != nil {
		fmt.Fprintf(c.stdout, "\n%s failed: %v. Press enter to go back.", c.args[0], err)
		buf := make([]byte, 1)
		_, _ = c.stdin.Read(buf)
	}
	return err
}

func (m *model) viewFixing() string {
	var b strings.Builder
	b.WriteString(sBold.Render("Getting what's missing") + "\n\n")
	for _, key := range fixOrder {
		it := m.findItem(key)
		if it == nil || (it.OK && !m.attempted[key]) {
			continue
		}
		switch {
		case it.OK:
			b.WriteString(sOK.Render("  ✓ ") + it.Name + "\n")
		case key == m.fixKey:
			b.WriteString("  " + m.spin.View() + " " + it.Name + "\n")
		default:
			b.WriteString(sDim.Render("  · "+it.Name) + "\n")
		}
	}
	b.WriteString("\n  " + sSoft.Render(truncate(m.fixMsg, m.width-8)))
	if m.fixTotal > 0 {
		b.WriteString("\n  " + m.bar.ViewAs(float64(m.fixDone)/float64(m.fixTotal)) + " " +
			sDim.Render(download.HumanBytes(m.fixDone)+" / "+download.HumanBytes(m.fixTotal)))
	} else if m.fixDone > 0 {
		b.WriteString("  " + sDim.Render(download.HumanBytes(m.fixDone)))
	}
	b.WriteString("\n\n" + sKeys.Render("ctrl+c quit"))
	return b.String()
}

func truncate(s string, n int) string {
	if n < 10 {
		n = 60
	}
	r := []rune(s)
	if len(r) > n {
		return string(r[:n-1]) + "…"
	}
	return s
}

// ---- done ----

func (m *model) doneKey(s string) (tea.Model, tea.Cmd) {
	switch s {
	case "q", "esc":
		return m, tea.Quit
	case "enter":
		if err := launch(m.info, m.install); err != nil {
			m.cleaned = "Couldn't start " + m.install.Name + ": " + err.Error()
			return m, nil
		}
		return m, tea.Quit
	case "d":
		if m.cleaned != "" {
			return m, nil
		}
		data := m.data()
		m.cleaned = "Deleting..."
		return m, func() tea.Msg {
			size := dirSize(data.Root)
			err := os.RemoveAll(data.Root)
			return cleanedMsg{msg: "Deleted " + data.Root + " (" + download.HumanBytes(size) + "). The game stays installed.", err: err}
		}
	}
	return m, nil
}

func dirSize(root string) int64 {
	var n int64
	_ = filepath.WalkDir(root, func(_ string, d os.DirEntry, err error) error {
		if err == nil && !d.IsDir() {
			if info, err := d.Info(); err == nil {
				n += info.Size()
			}
		}
		return nil
	})
	return n
}

func launch(info *sysinfo.Info, inst paths.Install) error {
	var cmd *exec.Cmd
	if info.OS == "darwin" {
		cmd = exec.Command("open", inst.AppBundle)
	} else {
		cmd = exec.Command(inst.Executable)
		cmd.Dir = inst.Prefix
	}
	detach(cmd)
	return cmd.Start()
}

func (m *model) viewDone() string {
	var b strings.Builder
	took := m.buildEnd.Sub(m.buildStart).Round(time.Second)
	b.WriteString(sOK.Render("✓ "+m.install.Name+" is built") + sDim.Render(fmt.Sprintf("  (%s, %d patches)", took, len(m.m.IDs(m.sel)))) + "\n\n")
	row := func(k, v string) { b.WriteString(fmt.Sprintf("  %-10s %s\n", sSoft.Render(k), v)) }
	if m.install.AppBundle != "" {
		row("App", m.install.AppBundle)
	} else {
		row("Game", m.install.Executable)
	}
	if m.install.Launcher != "" && !m.opts.NoLauncher {
		row("Launcher", m.install.Launcher)
	}
	row("Data", "the official osu! data folder (shared with osu!lazer)")
	row("Log", m.logPath)
	b.WriteString("\n")
	if m.cleaned != "" {
		b.WriteString("  " + sSoft.Render(m.cleaned) + "\n\n")
		b.WriteString(sKeys.Render("enter start " + m.install.Name + "  q quit"))
		return b.String()
	}
	b.WriteString(sDim.Render("  Build files (.NET SDK if downloaded, osu! source, NuGet cache) are in "+deps.Tildify(m.dataDir, m.info.HomeDir)+".") + "\n")
	b.WriteString(sDim.Render("  Keep them for faster rebuilds, or delete them; the next build downloads them again.") + "\n\n")
	b.WriteString(sKeys.Render("enter start " + m.install.Name + "  d delete build files  q quit"))
	return b.String()
}
