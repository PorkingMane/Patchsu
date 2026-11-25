package tui

import (
	"fmt"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/build"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
)

// locate is where the build called name installs: --prefix, else the chosen install folder, else the standard place.
func (m *model) locate(name string) paths.Install {
	return paths.Locate(m.info, name, m.opts.Prefix, m.cfg.InstallDir)
}

// installedFor returns patchsu-build.json of the build called name where it installs, or nil (read once).
func (m *model) installedFor(name string) *build.BuildInfo {
	if m.installed == nil {
		m.installed = map[string]*build.BuildInfo{}
	}
	bi, ok := m.installed[name]
	if !ok {
		bi = build.ReadBuildInfo(m.locate(name))
		m.installed[name] = bi
	}
	return bi
}

// list is the rows of the patch list: every patch except the servers' (those come with the server choice).
func (m *model) list() []*manifest.Patch { return m.m.ListPatches() }

// buildName is "Patchsu", or "Patchsu-<Server>" when the selection has a server's patches.
func (m *model) buildName() string { return m.m.BuildName(m.sel) }

// serverPreset reports whether the preset row is on a server preset (or the selection came from one).
func (m *model) serverPreset() bool {
	if !m.isCustom() {
		return m.m.Presets[m.presetIdx].Server
	}
	return m.m.ServerOf(m.sel) != nil
}

// server returns the server picked for server presets, or nil when the manifest has none.
func (m *model) server() *manifest.Server {
	if len(m.m.Servers) == 0 {
		return nil
	}
	return &m.m.Servers[((m.serverIdx%len(m.m.Servers))+len(m.m.Servers))%len(m.m.Servers)]
}

// serverRows is how many server rows sit between the preset row and the patches: one per server on server presets.
func (m *model) serverRows() int {
	if !m.serverPreset() {
		return 0
	}
	return len(m.m.Servers)
}

// rows is the number of cursor rows below the preset row (servers, then patches).
func (m *model) rows() int { return m.serverRows() + len(m.list()) }

// cursorServer is the server under the cursor, or -1.
func (m *model) cursorServer() int {
	if m.cursor >= 1 && m.cursor <= m.serverRows() {
		return m.cursor - 1
	}
	return -1
}

// cursorPatch is the patch under the cursor, or nil.
func (m *model) cursorPatch() *manifest.Patch {
	i := m.cursor - 1 - m.serverRows()
	if i < 0 || i >= len(m.list()) {
		return nil
	}
	return m.list()[i]
}

// initSelection restores the last selection, else the installed build's patches, else the first preset.
func (m *model) initSelection() {
	m.serverIdx = 0
	for i := range m.m.Servers {
		if m.m.Servers[i].Name == m.cfg.Server {
			m.serverIdx = i
		}
	}
	var ids []string
	if len(m.cfg.Patches) > 0 {
		ids = m.cfg.Patches
	} else if bi := m.installedFor(manifest.BaseName); bi != nil {
		ids = bi.Patches
	}
	m.sel = nil
	if len(ids) > 0 {
		if s, _, err := m.m.Resolve(ids); err == nil {
			m.sel = s
		}
	}
	if m.sel == nil {
		if len(m.m.Presets) > 0 {
			m.sel = m.m.PresetSelectionFor(&m.m.Presets[0], m.server())
		} else {
			m.sel = m.m.NewSelection()
		}
	}
	if sv := m.m.ServerOf(m.sel); sv != nil {
		for i := range m.m.Servers {
			if &m.m.Servers[i] == sv {
				m.serverIdx = i
			}
		}
	}
	m.syncPreset()
	if m.fromPreset == "" {
		m.fromPreset = m.cfg.Preset
	}
}

// syncPreset points the preset row at the preset matching the selection, or at Custom.
func (m *model) syncPreset() {
	m.presetIdx = len(m.m.Presets)
	if p := m.m.MatchPreset(m.sel); p != nil {
		for i := range m.m.Presets {
			if &m.m.Presets[i] == p {
				m.presetIdx = i
				m.fromPreset = p.Name
			}
		}
	}
}

func (m *model) isCustom() bool { return m.presetIdx >= len(m.m.Presets) }

// presetName is the preset name for the build, or "" for a custom selection.
func (m *model) presetName() string {
	if m.isCustom() {
		return ""
	}
	return m.m.Presets[m.presetIdx].Name
}

func (m *model) applyPreset(i int) {
	n := len(m.m.Presets)
	if n == 0 {
		return
	}
	i = ((i % n) + n) % n
	m.presetIdx = i
	m.sel = m.m.PresetSelectionFor(&m.m.Presets[i], m.server())
	m.fromPreset = m.m.Presets[i].Name
	m.notice = ""
}

// nextServer switches the server of a server build.
func (m *model) nextServer() {
	if !m.serverPreset() {
		return
	}
	if len(m.m.Servers) == 0 {
		m.notice = "No custom server patches are published yet."
		return
	}
	m.pickServer(m.serverIdx + 1)
}

// pickServer makes server i the server of the build.
func (m *model) pickServer(i int) {
	m.serverIdx = i
	m.m.SetServer(m.sel, m.server())
	m.notice = ""
	if !m.isCustom() {
		return // the preset stays the same, only its server changed
	}
	m.syncPreset()
}

func (m *model) pickerKey(s string) (tea.Model, tea.Cmd) {
	before := m.serverRows()
	mod, cmd := m.pickerAction(s)
	if after := m.serverRows(); after != before {
		if m.cursor > before {
			m.cursor += after - before // stay on the same patch
		} else if m.cursor > after {
			m.cursor = 0
		}
	}
	m.cursor = min(max(m.cursor, 0), m.rows())
	return mod, cmd
}

func (m *model) pickerAction(s string) (tea.Model, tea.Cmd) {
	n := m.rows()
	switch s {
	case "q", "esc":
		return m, tea.Quit
	case "up", "k":
		if m.cursor > 0 {
			m.cursor--
		}
	case "down", "j":
		if m.cursor < n {
			m.cursor++
		}
	case "home", "g":
		m.cursor = 0
	case "end", "G":
		m.cursor = n
	case "tab":
		m.nextPreset(1)
	case "shift+tab":
		m.nextPreset(-1)
	case "left", "h":
		if m.cursor == 0 {
			m.nextPreset(-1)
		}
	case "right", "l":
		if m.cursor == 0 {
			m.nextPreset(1)
		}
	case " ", "x":
		if i := m.cursorServer(); i >= 0 {
			m.pickServer(i)
		} else if p := m.cursorPatch(); p != nil {
			m.toggle(p.ID)
		}
	case "s":
		m.nextServer()
	case "a":
		sv := m.m.ServerOf(m.sel)
		m.sel = m.m.AllSelection()
		m.m.SetServer(m.sel, sv)
		m.notice = ""
		m.syncPreset()
	case "n":
		m.sel = m.m.NewSelection()
		m.notice = ""
		m.syncPreset()
	case "enter":
		if m.serverPreset() && m.m.ServerOf(m.sel) == nil {
			m.notice = "No custom server patches are published yet, so there's nothing to connect to. Pick another preset."
			return m, nil
		}
		m.install = m.locate(m.buildName())
		m.cfg.Patches = m.m.IDs(m.sel)
		m.cfg.Preset = m.fromPreset
		if sv := m.m.ServerOf(m.sel); sv != nil {
			m.cfg.Server = sv.Name
		}
		_ = m.cfg.Save()
		if m.dataDir == "" {
			m.openDataDir(scrPicker)
			return m, nil
		}
		return m, m.toCheck()
	}
	return m, nil
}

// nextPreset steps through the presets. From Custom, it starts at the preset the selection came from.
func (m *model) nextPreset(dir int) {
	if m.isCustom() {
		start := -1
		for i := range m.m.Presets {
			if m.m.Presets[i].Name == m.fromPreset {
				start = i
			}
		}
		if start < 0 {
			if dir > 0 {
				m.applyPreset(0)
			} else {
				m.applyPreset(len(m.m.Presets) - 1)
			}
			return
		}
		m.applyPreset(start)
		return
	}
	m.applyPreset(m.presetIdx + dir)
}

func (m *model) toggle(id string) {
	p := m.m.Patch(id)
	if p.Required {
		m.notice = p.Label() + " is required by every other patch."
		return
	}
	if m.sel[id] {
		removed := m.m.Disable(m.sel, id)
		m.notice = ""
		if len(removed) > 0 {
			m.notice = fmt.Sprintf("Also disabled %d: %s", len(removed), m.m.Labels(removed))
		}
	} else {
		added := m.m.Enable(m.sel, id)
		m.notice = ""
		if len(added) > 0 {
			m.notice = fmt.Sprintf("Also enabled %d: %s", len(added), m.m.Labels(added))
		}
	}
	m.syncPreset()
}

func (m *model) viewPicker() string {
	var b strings.Builder
	width := m.width - 4
	if width <= 0 {
		width = 90
	}

	// Preset row
	parts := []string{}
	for i, p := range m.m.Presets {
		if i == m.presetIdx {
			name := "‹ " + p.Name + " ›"
			parts = append(parts, sPreset.Render(name))
		} else {
			parts = append(parts, sSoft.Render(p.Name))
		}
	}
	custom := "Custom"
	if m.isCustom() {
		if m.fromPreset != "" {
			custom = "Custom (from " + m.fromPreset + ")"
		}
		parts = append(parts, sPreset.Render("‹ "+custom+" ›"))
	} else {
		parts = append(parts, sDim.Render(custom))
	}
	cur := "  "
	if m.cursor == 0 {
		cur = sCursor.Render("› ")
	}
	// Flow the presets over as many lines as the width needs.
	lead := cur + sSection.Render("Preset ")
	indent := strings.Repeat(" ", lipgloss.Width(lead))
	line := lead
	used := lipgloss.Width(lead)
	for i, part := range parts {
		w := lipgloss.Width(part)
		if i > 0 && used+3+w > width {
			b.WriteString(line + "\n")
			line, used = indent+part, len(indent)+w
			continue
		}
		if i > 0 {
			line += "   "
			used += 3
		}
		line += part
		used += w
	}
	b.WriteString(line + "\n")
	name := m.buildName()
	if m.serverPreset() && m.m.ServerOf(m.sel) == nil {
		name = manifest.BaseName + "-<Server>" // nothing to build until a server's patches exist
	} else if bi := m.installedFor(name); bi != nil {
		what := bi.Preset
		if what == "" {
			what = fmt.Sprintf("%d patches", len(bi.Patches))
		}
		b.WriteString("  " + sDim.Render("Installed now: "+name+", "+what+" ("+bi.Release+", "+bi.Built.Local().Format("2006-01-02")+")") + "\n")
	}
	b.WriteString("\n")

	// Server rows (server presets only)
	svLines := 0 // lines taken by the server section
	if m.serverPreset() {
		b.WriteString("  " + sSection.Render("Servers available") + "\n")
		if len(m.m.Servers) == 0 {
			b.WriteString("    " + sWarn.Render("none yet") + sDim.Render("  no custom server patches are published yet") + "\n")
		}
		svW := 0
		for i := range m.m.Servers {
			svW = max(svW, lipgloss.Width(m.m.Servers[i].Name))
		}
		for i := range m.m.Servers {
			b.WriteString(m.serverRow(i, svW) + "\n")
		}
		b.WriteString("\n")
		svLines = max(1, len(m.m.Servers)) + 2
	}

	// Patch rows
	b.WriteString("  " + sSection.Render("Patches available") + "\n")
	list := m.list()
	nameW := 0
	for _, p := range list {
		nameW = max(nameW, lipgloss.Width(p.Name))
	}
	rows := make([]string, 0, len(list))
	for i, p := range list {
		rows = append(rows, m.patchRow(m.serverRows()+i+1, p, nameW))
	}
	listH := len(rows)
	if m.height > 0 {
		listH = min(listH, max(5, m.height-19-svLines))
	}
	start := 0
	if pi := m.cursor - 1 - m.serverRows(); pi >= listH {
		start = pi - listH + 1
	}
	if start > 0 {
		b.WriteString(sDim.Render("    ↑ more") + "\n")
	}
	for _, r := range rows[start:min(len(rows), start+listH)] {
		b.WriteString(r + "\n")
	}
	if start+listH < len(rows) {
		b.WriteString(sDim.Render("    ↓ more") + "\n")
	}

	// Description of the row under the cursor
	b.WriteString("\n")
	if m.cursor == 0 {
		if m.isCustom() {
			b.WriteString(rule(width, custom) + "\n")
			b.WriteString(wrap("Your own selection. Tab or ←/→ switches to a preset.", width) + "\n")
		} else {
			p := m.m.Presets[m.presetIdx]
			b.WriteString(rule(width, p.Name) + "\n")
			b.WriteString(wrap(p.Description, width) + "\n")
			b.WriteString(sDim.Render(wrap("Patches: "+strings.Join(m.m.IDs(m.sel), ", "), width)) + "\n")
		}
	} else if i := m.cursorServer(); i >= 0 {
		sv := &m.m.Servers[i]
		b.WriteString(rule(width, sv.Name) + "\n")
		if sv.Description != "" {
			b.WriteString(wrap(sv.Description, width) + "\n")
		}
		b.WriteString(sDim.Render(wrap("Patches: "+m.m.Labels(sv.Patches)+" · builds "+sv.BuildName()+", installed next to Patchsu", width)) + "\n")
	} else {
		p := m.cursorPatch()
		b.WriteString(rule(width, p.Label()) + "\n")
		b.WriteString(wrap(p.Description, width) + "\n")
		if len(p.Needs) > 0 {
			b.WriteString(sDim.Render("Needs: "+m.m.Labels(p.Needs)) + "\n")
		}
	}

	// Notice + keys
	b.WriteString("\n")
	if m.notice != "" {
		b.WriteString(sWarn.Render(wrap(m.notice, width)) + "\n")
	} else {
		b.WriteString("\n")
	}
	count := len(m.m.IDs(m.sel))
	b.WriteString(sSoft.Render(fmt.Sprintf("%d patches selected", count)) + sDim.Render(" · builds ") + sBold.Render(name) + "\n")
	b.WriteString(m.systemLine(width) + "\n\n")
	keys := "↑/↓ move  space toggle  tab/←/→ preset  "
	b.WriteString(sKeys.Render(keys + "a all  n none  enter continue  q quit"))
	return b.String()
}

// serverRow is one row of the server list: a radio button, the server's name and patches, and the build it makes.
func (m *model) serverRow(i, nameW int) string {
	sv := &m.m.Servers[i]
	picked := m.m.ServerOf(m.sel) == sv
	box := "( )"
	style := lipgloss.NewStyle()
	if picked {
		box, style = "(•)", sBold
	}
	cur := "  "
	if m.cursor == i+1 {
		cur = sCursor.Render("› ")
		style = style.Foreground(accent)
	}
	main := fmt.Sprintf("%s %-*s  %s  ", box, nameW, sv.Name, strings.Join(sv.Patches, ", "))
	return cur + style.Render(main) + sDim.Render("builds "+sv.BuildName())
}

func (m *model) patchRow(row int, p *manifest.Patch, nameW int) string {
	selected := m.sel[p.ID]
	missing := m.m.Missing(m.sel, p.ID)
	grayed := !selected && len(missing) > 0

	box := "[ ]"
	switch {
	case p.Required:
		box = "[■]"
	case selected:
		box = "[x]"
	}
	hint := p.Note
	switch {
	case p.Required:
		hint = "required"
	case grayed:
		hint = "needs " + strings.Join(missing, ", ")
	}
	text := fmt.Sprintf("%s %-5s %-*s  %s", box, p.ID, nameW, p.Name, hint)

	cur := "  "
	style := lipgloss.NewStyle()
	switch {
	case grayed:
		style = sDim
	case p.Required:
		style = sSoft
	case selected:
		style = sBold
	}
	if row == m.cursor {
		cur = sCursor.Render("› ")
		if !grayed {
			style = style.Foreground(accent)
		}
	}
	// Keep the hint column dim on normal rows.
	if !grayed && hint != "" {
		main := fmt.Sprintf("%s %-5s %-*s  ", box, p.ID, nameW, p.Name)
		return cur + style.Render(main) + sDim.Render(hint)
	}
	return cur + style.Render(text)
}

// systemLine is the result of the silent system check, shown under the patch list.
func (m *model) systemLine(width int) string {
	if m.bgChecking || m.report == nil {
		return m.spin.View() + sDim.Render(" checking your system...")
	}
	r := m.report
	short := map[string]string{"dotnet": ".NET", "git": "git", "source": "osu! source", "patches": "patches"}
	var parts []string
	for _, it := range r.Items {
		name := short[it.Key]
		if it.OK {
			parts = append(parts, sOK.Render("✓")+sDim.Render(" "+name))
		} else {
			parts = append(parts, sBad.Render("✗")+sDim.Render(" "+name))
		}
	}
	size := "~" + download.HumanBytes(r.Needed) + " needed"
	if r.Free >= 0 {
		size += ", " + download.HumanBytes(r.Free) + " free"
	}
	sizeStyle := sDim
	if !r.DiskOK() {
		sizeStyle = sBad
	}
	line := sSection.Render("System ") + strings.Join(parts, "  ") + sDim.Render("  ·  ") + sizeStyle.Render(size)
	if missing := len(r.Missing()); missing > 0 {
		line += sDim.Render(fmt.Sprintf("  (%d to get)", missing))
	}
	if lipgloss.Width(line) > width {
		line = sSection.Render("System ") + strings.Join(parts, "  ") + "\n       " + sizeStyle.Render(size)
	}
	return line
}
