package tui

import (
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"

	tea "github.com/charmbracelet/bubbletea"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/config"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/deps"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// instOption is one choice on the install location screen. parent is the folder the game goes into ("" = standard).
type instOption struct {
	label  string
	parent string
	note   string
	typed  bool // "Somewhere else": the parent comes from the text field
}

// openInstallDir shows the install location screen: the standard place, next to osu!lazer, or a typed folder.
func (m *model) openInstallDir() {
	osu := paths.FindOsuData(m.info)
	m.instOpts = []instOption{
		{label: "Standard", parent: "", note: map[string]string{
			"linux": "your data folder (XDG_DATA_HOME)", "windows": `%LOCALAPPDATA%, like osu!lazer's own installer`,
			"darwin": "Applications in your home folder"}[m.info.OS]},
		{label: "Next to osu! data", parent: osu.Parent, note: m.osuNote(osu)},
		{label: "Somewhere else", typed: true},
	}
	m.instInput = field{placeholder: "type a folder, e.g. " + filepath.Join(m.info.HomeDir, "Games")}
	m.instCursor = 0
	switch m.cfg.InstallDir {
	case "":
	case osu.Parent:
		m.instCursor = 1
	default:
		m.instCursor = 2
		m.instInput.value = []rune(m.cfg.InstallDir)
	}
	m.instErr = ""
	m.focusInstall()
	m.scr = scrInstallDir
}

func (m *model) focusInstall() {
	m.instInput.focused = m.instOpts[m.instCursor].typed
}

// instParent is the folder option i installs into, and an error for a typed folder that can't be used as is.
func (m *model) instParent(i int) (string, error) {
	o := m.instOpts[i]
	if !o.typed {
		return o.parent, nil
	}
	p := expandPath(string(m.instInput.value), m.info.HomeDir)
	if p == "" {
		return "", fmt.Errorf("type a folder first")
	}
	if !filepath.IsAbs(p) {
		return "", fmt.Errorf("type a full path (starting with %s)", map[bool]string{true: `C:\ or ~`, false: "/ or ~"}[m.info.OS == "windows"])
	}
	return p, nil
}

// instProblem says why option i can't be used, or "".
func (m *model) instProblem(i int) string {
	parent, err := m.instParent(i)
	if err != nil {
		return err.Error()
	}
	in := paths.InstallIn(m.info, m.install.Name, parent)
	switch {
	case !sysinfo.Writable(in.Root()):
		return "not writable"
	case parent != "" && paths.Occupied(in): // the standard place is always Patchsu's
		return "already holds something that isn't Patchsu"
	}
	return ""
}

func (m *model) installDirKey(k tea.KeyMsg) (tea.Model, tea.Cmd) {
	s := k.String()
	typing := m.instOpts[m.instCursor].typed
	switch s {
	case "esc":
		m.scr = scrCheck
		return m, nil
	case "up", "shift+tab":
		m.instCursor = (m.instCursor + len(m.instOpts) - 1) % len(m.instOpts)
		m.instErr = ""
		m.focusInstall()
		return m, nil
	case "down", "tab":
		m.instCursor = (m.instCursor + 1) % len(m.instOpts)
		m.instErr = ""
		m.focusInstall()
		return m, nil
	case "enter":
		if p := m.instProblem(m.instCursor); p != "" {
			m.instErr = p
			return m, nil
		}
		parent, _ := m.instParent(m.instCursor)
		m.cfg.InstallDir = parent
		_ = m.cfg.Save()
		m.installed = nil // the installed builds are read from the new place
		m.install = m.locate(m.install.Name)
		m.scr = scrCheck
		return m, nil
	}
	if typing {
		if m.instInput.key(k) {
			m.instErr = ""
		}
		return m, nil
	}
	switch s {
	case "q":
		return m, tea.Quit
	case "k":
		return m.installDirKey(tea.KeyMsg{Type: tea.KeyUp})
	case "j":
		return m.installDirKey(tea.KeyMsg{Type: tea.KeyDown})
	}
	return m, nil
}

func (m *model) viewInstallDir() string {
	var b strings.Builder
	home := m.info.HomeDir
	b.WriteString(sBold.Render("Where should "+m.install.Name+" be installed?") + "\n\n")
	for i, o := range m.instOpts {
		cur := "  "
		if i == m.instCursor {
			cur = sCursor.Render("› ")
		}
		label := fmt.Sprintf("%-20s ", o.label)
		if o.typed {
			if i == m.instCursor {
				label = sBold.Render(label)
			}
			b.WriteString(cur + label + m.instInput.View() + "\n")
			continue
		}
		where := deps.Tildify(paths.InstallIn(m.info, m.install.Name, o.parent).Root(), home)
		line := label + where
		problem := m.instProblem(i)
		switch {
		case problem != "":
			line = sDim.Render(line + "  (" + problem + ")")
		case i == m.instCursor:
			line = sBold.Render(line)
		}
		if o.note != "" {
			line += "\n" + strings.Repeat(" ", 23) + sDim.Render(o.note)
		}
		b.WriteString(cur + line + "\n")
	}
	b.WriteString("\n")
	if m.instOpts[m.instCursor].typed {
		if parent, err := m.instParent(m.instCursor); err == nil {
			in := paths.InstallIn(m.info, m.install.Name, parent)
			b.WriteString(sDim.Render("Installs into "+deps.Tildify(in.Root(), home)) + "\n")
		} else {
			b.WriteString("\n")
		}
	} else {
		b.WriteString("\n")
	}
	if m.instErr != "" {
		b.WriteString(sWarn.Render("Can't install there: "+m.instErr) + "\n")
	} else {
		b.WriteString("\n")
	}
	game := paths.GameFolder(m.info, m.install.Name)
	b.WriteString(sDim.Render(wrap("The game gets its own "+game+" folder there; server builds get theirs next to it. "+
		"The menu entry and your osu! data stay where they are.", m.width-8)) + "\n")
	b.WriteString(sDim.Render("Remembered in "+deps.Tildify(config.Path(), home)+".") + "\n\n")
	b.WriteString(sKeys.Render("↑/↓ move  type a folder on the last row  enter choose  esc back"))
	return b.String()
}

var winEnvRe = regexp.MustCompile(`%([A-Za-z_][A-Za-z0-9_]*)%`)

// expandPath turns a typed folder into a clean path: quotes trimmed, ~ is the home folder, $VAR and %VAR% expanded.
func expandPath(p, home string) string {
	p = strings.Trim(strings.TrimSpace(p), `"'`)
	if p == "" {
		return ""
	}
	if p == "~" {
		p = home
	} else if strings.HasPrefix(p, "~/") || strings.HasPrefix(p, `~\`) {
		p = filepath.Join(home, p[2:])
	}
	p = winEnvRe.ReplaceAllStringFunc(p, func(v string) string { return os.Getenv(v[1 : len(v)-1]) })
	return filepath.Clean(os.ExpandEnv(p))
}

// field is a one-line text field: typing and pasting add at the end, backspace and ctrl+w/ctrl+u delete.
type field struct {
	value       []rune
	placeholder string
	focused     bool
}

// key applies a key press and reports whether the text changed.
func (f *field) key(k tea.KeyMsg) bool {
	switch k.Type {
	case tea.KeyRunes, tea.KeySpace:
		f.value = append(f.value, []rune(strings.ReplaceAll(string(k.Runes), "\n", ""))...)
	case tea.KeyBackspace:
		if len(f.value) == 0 {
			return false
		}
		f.value = f.value[:len(f.value)-1]
	case tea.KeyCtrlW: // back to the previous separator
		v := strings.TrimRight(string(f.value), `/\`)
		i := strings.LastIndexAny(v, `/\`)
		f.value = []rune(v[:i+1])
	case tea.KeyCtrlU:
		f.value = nil
	default:
		return false
	}
	return true
}

func (f *field) View() string {
	cursor := ""
	if f.focused {
		cursor = sCursor.Render("█")
	}
	if len(f.value) == 0 {
		return cursor + sDim.Render(f.placeholder)
	}
	return string(f.value) + cursor
}
