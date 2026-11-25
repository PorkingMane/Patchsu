package tui

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/build"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/deps"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
)

func (m *model) fixable() []deps.Item {
	var out []deps.Item
	for _, it := range m.report.Missing() {
		if it.Fix != nil && it.Fix.Manual == "" {
			out = append(out, it)
		}
	}
	return out
}

func (m *model) checkKey(s string) (tea.Model, tea.Cmd) {
	switch s {
	case "q":
		return m, tea.Quit
	case "esc":
		m.fixErr, m.waitRecheck = nil, ""
		m.scr = scrPicker
		return m, nil
	case "r":
		m.fixErr, m.waitRecheck = nil, ""
		return m, m.runCheck()
	case "i":
		if m.opts.Prefix == "" {
			m.openInstallDir()
		}
	case "f":
		if m.opts.DataDir == "" {
			m.openDataDir(scrCheck)
		}
	case "up", "k":
		m.checkCursor = 0
	case "down", "j":
		m.checkCursor = 1
	case "enter":
		if m.report.Ready() {
			return m, m.startBuild()
		}
		if !m.report.DiskOK() || len(m.fixable()) == 0 {
			return m, nil
		}
		if m.checkCursor == 1 {
			m.exitMessage = m.manualInstructions()
			return m, tea.Quit
		}
		m.fixErr, m.waitRecheck = nil, ""
		m.attempted = map[string]bool{}
		m.scr = scrFixing
		return m, m.nextFix()
	}
	return m, nil
}

func (m *model) viewCheck() string {
	r := m.report
	var b strings.Builder
	b.WriteString(sSoft.Render("Detected: ") + r.Info.Summary() + "\n")
	b.WriteString(sSoft.Render("Data:     ") + deps.Tildify(m.dataDir, m.info.HomeDir))
	if m.opts.DataDir != "" {
		b.WriteString(sDim.Render("  (--data-dir)") + "\n")
	} else {
		b.WriteString(sDim.Render("  f to change") + "\n")
	}
	b.WriteString(sSoft.Render("Install:  ") + deps.Tildify(m.install.Root(), m.info.HomeDir))
	if m.opts.Prefix != "" {
		b.WriteString(sDim.Render("  (--prefix)"))
	} else {
		b.WriteString(sDim.Render("  i to change"))
	}
	b.WriteString("\n\n")

	group := ""
	for _, it := range r.Items {
		if it.Group != group {
			if group != "" {
				b.WriteString("\n")
			}
			group = it.Group
			b.WriteString("  " + sSection.Render(group) + "\n")
		}
		mark := sOK.Render("✓")
		detail := sDim.Render(it.Detail)
		if !it.OK {
			mark = sBad.Render("✗")
			detail = sSoft.Render(it.Detail)
		}
		b.WriteString(fmt.Sprintf("  %s %-26s %s\n", mark, it.Name, detail))
	}
	free := "unknown"
	if r.Free >= 0 {
		free = download.HumanBytes(r.Free)
	}
	var parts []string
	for _, p := range r.Parts {
		parts = append(parts, p.Name+" "+download.HumanBytes(p.Bytes))
	}
	disk := fmt.Sprintf("Disk space: ~%s needed (%s), %s free", download.HumanBytes(r.Needed), strings.Join(parts, " + "), free)
	if r.DiskOK() {
		b.WriteString("\n  " + sDim.Render(disk) + "  " + sOK.Render("✓") + "\n\n")
	} else {
		b.WriteString("\n  " + sBad.Render(disk+"  ✗  free some space or choose another data folder (--data-dir)") + "\n\n")
	}

	if m.fixErr != nil {
		b.WriteString(sBad.Render("Failed: ") + wrap(m.fixErr.Error(), m.width-12) + "\n\n")
	}
	if m.waitRecheck != "" {
		b.WriteString(sWarn.Render(m.waitRecheck) + "\n\n")
	}

	if r.Ready() {
		what := m.presetName()
		if what == "" {
			what = "custom selection"
		}
		b.WriteString(sOK.Render("Everything is ready.") + fmt.Sprintf(" Build %s: %d patches (%s)?", m.install.Name, len(m.m.IDs(m.sel)), what) + "\n\n")
		b.WriteString(sKeys.Render("enter build  " + m.installKey() + "esc back  q quit"))
		return b.String()
	}

	for _, it := range r.Missing() {
		if it.Fix != nil && it.Fix.Manual != "" {
			b.WriteString(sWarn.Render(it.Name+": ") + it.Fix.Manual + "\n")
		}
	}
	fix := m.fixable()
	if len(fix) > 0 && r.DiskOK() {
		b.WriteString(sBold.Render("Some things are missing:") + "\n")
		for _, it := range fix {
			b.WriteString(fmt.Sprintf("  %s %-24s %s\n", sBad.Render("✗"), it.Name, sSoft.Render(it.Fix.Summary)))
		}
		b.WriteString("\n" + "Get them now? Downloads go into the data folder; nothing else is installed except git through your package manager.\n\n")
		opts := []string{"Yes", "No, I'll do it myself  (prints the steps and exits)"}
		for i, o := range opts {
			if i == m.checkCursor {
				b.WriteString(sCursor.Render("› ") + sBold.Render(o) + "\n")
			} else {
				b.WriteString("  " + o + "\n")
			}
		}
		b.WriteString("\n" + sKeys.Render("↑/↓ move  enter choose  r check again  "+m.installKey()+"esc back"))
		return b.String()
	}
	b.WriteString("\n" + sKeys.Render("r check again  "+m.installKey()+"esc back  q quit"))
	return b.String()
}

// installKey is the key hints for the install location and data folder, without those fixed by --prefix / --data-dir.
func (m *model) installKey() string {
	k := ""
	if m.opts.Prefix == "" {
		k += "i install location  "
	}
	if m.opts.DataDir == "" {
		k += "f data folder  "
	}
	return k
}

func (m *model) manualInstructions() string {
	var b strings.Builder
	b.WriteString("To build Patchsu, get these first:\n")
	for _, it := range m.report.Missing() {
		switch it.Key {
		case "git":
			if it.Fix != nil && it.Fix.Command != nil {
				b.WriteString("  git:      " + strings.Join(it.Fix.Command, " ") + "\n")
			} else {
				b.WriteString("  git:      https://git-scm.com/downloads\n")
			}
		case "dotnet":
			b.WriteString("  .NET SDK: https://dotnet.microsoft.com/download/dotnet/" + m.m.DotnetSDK + "\n")
		case "source":
			b.WriteString("  osu! source: downloaded by Patchsu-CLI once git is installed\n")
		case "patches":
			b.WriteString("  patches:  downloaded by Patchsu-CLI\n")
		}
	}
	b.WriteString("Then run Patchsu-CLI again.")
	return b.String()
}

// ---- build ----

func (m *model) startBuild() tea.Cmd {
	data := m.data()
	_ = os.MkdirAll(data.Logs(), 0o755)
	m.logPath = filepath.Join(data.Logs(), "build-"+time.Now().Format("20060102-150405")+".log")
	logFile, err := os.Create(m.logPath)
	if err != nil {
		m.fixErr = err
		return nil
	}
	tools := m.report.Tools
	m.builder = &build.Builder{
		Info: m.info, Source: m.src, Tools: &tools, Install: m.install, Selection: m.sel.Clone(),
		Preset: m.presetName(), CLIVersion: m.opts.Version, Launcher: !m.opts.NoLauncher, Log: logFile,
	}
	m.steps = m.builder.Steps()
	m.states = make([]build.State, len(m.steps))
	m.details = make([]string, len(m.steps))
	m.logLines = nil
	m.buildErr = nil
	m.quitArmed = false
	m.buildStart = time.Now()
	m.scr = scrBuild

	ctx, cancel := context.WithCancel(context.Background())
	m.cancel = cancel
	events := make(chan build.Event, 512)
	b, prog := m.builder, m.prog
	go func() {
		err := b.Run(ctx, events)
		logFile.Close()
		prog.Send(buildDoneMsg{err})
	}()
	go func() {
		for ev := range events {
			prog.Send(buildEventMsg{ev})
		}
	}()
	return m.spin.Tick
}

func (m *model) applyEvent(ev build.Event) {
	if ev.Step < 0 || ev.Step >= len(m.states) {
		return
	}
	if ev.Line != "" {
		m.logLines = append(m.logLines, ev.Line)
		if len(m.logLines) > 3000 {
			m.logLines = m.logLines[len(m.logLines)-2000:]
		}
		if strings.TrimSpace(ev.Line) != "" {
			m.details[ev.Step] = strings.TrimSpace(ev.Line)
		}
		return
	}
	m.states[ev.Step] = ev.State
	if ev.Detail != "" || ev.State == build.Done {
		m.details[ev.Step] = ev.Detail
	}
}

func (m *model) viewBuild() string {
	var b strings.Builder
	what := m.presetName()
	if what == "" {
		what = "Custom"
	}
	b.WriteString(sBold.Render("Building "+m.install.Name) + sSoft.Render(" · "+what+" · "+m.src.Describe()) + "\n\n")
	for i, st := range m.steps {
		var mark string
		name := st.Name
		switch m.states[i] {
		case build.Done:
			mark = sOK.Render("✓")
		case build.Running:
			mark = m.spin.View()
			name = sBold.Render(name)
		case build.Failed:
			mark = sBad.Render("✗")
			name = sBad.Render(name)
		default:
			mark = sDim.Render("·")
			name = sDim.Render(name)
		}
		line := "  " + mark + " " + name
		if m.states[i] == build.Running && m.details[i] != "" {
			line += "  " + sDim.Render(truncate(m.details[i], max(20, m.width-len(st.Name)-14)))
		}
		b.WriteString(line + "\n")
	}
	end := time.Now()
	if !m.buildEnd.IsZero() {
		end = m.buildEnd
	}
	b.WriteString("\n  " + sDim.Render("elapsed "+end.Sub(m.buildStart).Round(time.Second).String()) + "\n")

	if m.showLog {
		n := 15
		if m.height > 0 {
			n = max(5, m.height-len(m.steps)-14)
		}
		from := max(0, len(m.logLines)-n)
		b.WriteString("\n" + rule(m.width-4, "log") + "\n")
		for _, l := range m.logLines[from:] {
			b.WriteString(sDim.Render(truncate(l, m.width-6)) + "\n")
		}
	}
	b.WriteString("\n")
	if m.buildErr != nil {
		b.WriteString(sBad.Render("Build failed: ") + wrap(m.buildErr.Error(), m.width-20) + "\n")
		b.WriteString(sDim.Render("Full log: "+m.logPath) + "\n\n")
		b.WriteString(sKeys.Render("l show/hide log  q quit"))
		return b.String()
	}
	if m.quitArmed {
		b.WriteString(sWarn.Render("Press ctrl+c again to stop the build.") + "\n")
	}
	b.WriteString(sKeys.Render("l show/hide log  ctrl+c stop"))
	return b.String()
}
