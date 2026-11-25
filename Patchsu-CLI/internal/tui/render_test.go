package tui

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"testing/fstest"

	tea "github.com/charmbracelet/bubbletea"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/config"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/release"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// TestPickerFlow drives the picker with key presses and checks the notices. Set PATCHSU_SHOW=1 to print the screens.
func TestPickerFlow(t *testing.T) {
	src, err := release.FromDir("../../../Patchsu/patches")
	if err != nil {
		t.Skip(err)
	}
	m := newTestModel(t, src)
	show := func(label string) string {
		v := m.View()
		if os.Getenv("PATCHSU_SHOW") != "" {
			t.Logf("---- %s ----\n%s", label, v)
		}
		return v
	}
	key := func(s string) { press(m, s) }
	if v := show("start"); !strings.Contains(v, "‹ Vanilla+ ›") || !strings.Contains(v, "builds Patchsu") {
		t.Fatal("first preset not selected")
	}
	// Cursor to 0006 (row 7, after 0005b) and select it: 0002 gets pulled in.
	for i := 0; i < 7; i++ {
		key("down")
	}
	key(" ")
	if v := show("0006 on"); !strings.Contains(v, "Also enabled 1: 0002 Local profiles") || !strings.Contains(v, "Custom (from Vanilla+)") {
		t.Fatalf("notice/custom missing:\n%s", v)
	}
	// Cursor to 0002 (row 2) and deselect: 0006 goes too.
	for i := 0; i < 5; i++ {
		key("k")
	}
	key(" ")
	// That's Vanilla+ again, so the preset is recognised once more.
	if v := show("0002 off"); !strings.Contains(v, "Also disabled 1: 0006") || !strings.Contains(v, "‹ Vanilla+ ›") {
		t.Fatalf("disable notice / preset missing:\n%s", v)
	}
	key("tab")
	if v := show("tab"); !strings.Contains(v, "‹ Local play ›") {
		t.Fatalf("tab didn't move presets:\n%s", v)
	}
	// Custom server: the published server (Mamestagram, 0101) is chosen and names the build.
	key("tab")
	// The server list sits above the patches, and the cursor stays on 0002.
	if v := show("custom server"); !strings.Contains(v, "‹ Custom server ›") || !strings.Contains(v, "Servers available") ||
		!strings.Contains(v, "(•) Mamestagram") || !strings.Contains(v, "builds Patchsu-Mamestagram") ||
		!strings.Contains(v, "› [ ] 0002") || strings.Contains(v, "Mamestagram server") {
		t.Fatalf("server preset / server list missing (or the server patch shown in the patches):\n%s", v)
	}
	if v := show("custom server"); strings.Index(v, "Servers available") > strings.Index(v, "Patches available") {
		t.Fatalf("servers must come before the patches:\n%s", v)
	}
	// Back to a preset without servers: the server rows go, the cursor stays on 0002.
	key("shift+tab")
	if v := show("local play again"); strings.Contains(v, "Servers available") || !strings.Contains(v, "› [x] 0002") {
		t.Fatalf("server list still shown or cursor moved:\n%s", v)
	}
}

// TestPickerWithoutServers: a release without servers says so on the server preset, and enter stays on the picker.
func TestPickerWithoutServers(t *testing.T) {
	meta := `{"osuCommit":"abc",
 "patches":[{"id":"0001","file":"1.patch","name":"Online restrictions","required":true},
  {"id":"0005","file":"5.patch","name":"Interface","needs":["0001"]}],
 "presets":[{"name":"Vanilla+","patches":["0005"]},{"name":"Custom server","patches":["0005"],"server":true}]}`
	fsys := fstest.MapFS{"patches.meta.json": {Data: []byte(meta)}, "1.patch": {Data: []byte("1")}, "5.patch": {Data: []byte("5")}}
	src, err := release.FromFS(fsys)
	if err != nil {
		t.Fatal(err)
	}
	m := newTestModel(t, src)
	press(m, "tab")
	if v := m.View(); !strings.Contains(v, "‹ Custom server ›") || !strings.Contains(v, "none yet") {
		t.Fatalf("server preset / server line missing:\n%s", v)
	}
	press(m, "enter")
	if v := m.View(); m.scr != scrPicker || !strings.Contains(v, "No custom server patches are published yet") {
		t.Fatalf("server build without a server went on:\n%s", v)
	}
}

// TestServerPicker uses a release with two servers: the server line, switching servers and the build name.
func TestServerPicker(t *testing.T) {
	meta := `{"osuCommit":"abc",
 "patches":[{"id":"0001","file":"1.patch","name":"Online restrictions","required":true},
  {"id":"0005","file":"5.patch","name":"Interface","needs":["0001"]},
  {"id":"0009","file":"9.patch","name":"More mods","needs":["0001"]},
  {"id":"0101","file":"g.patch","name":"Gatari connection","needs":["0001"]},
  {"id":"0111","file":"t.patch","name":"Torii connection","needs":["0001"]}],
 "presets":[{"name":"Vanilla+","patches":["0005"]},{"name":"Local play","patches":"*"},
  {"name":"Custom server","patches":["0005"],"server":true}],
 "servers":[{"name":"Gatari","description":"osu!gatari","patches":["0101"]},{"name":"Torii","patches":["0111"]}]}`
	fsys := fstest.MapFS{"patches.meta.json": {Data: []byte(meta)}}
	for _, f := range []string{"1", "5", "9", "g", "t"} {
		fsys[f+".patch"] = &fstest.MapFile{Data: []byte(f)}
	}
	src, err := release.FromFS(fsys)
	if err != nil {
		t.Fatal(err)
	}
	m := newTestModel(t, src)
	v := m.View()
	if strings.Contains(v, "Gatari connection") {
		t.Fatalf("server patches must not be in the list:\n%s", v)
	}
	press(m, "shift+tab") // from Vanilla+ back round to Custom server
	if v = m.View(); !strings.Contains(v, "(•) Gatari") || !strings.Contains(v, "( ) Torii") || !strings.Contains(v, "builds Patchsu-Gatari") {
		t.Fatalf("Gatari not chosen:\n%s", v)
	}
	press(m, "s")
	if v = m.View(); !strings.Contains(v, "(•) Torii") || !strings.Contains(v, "‹ Custom server ›") || m.buildName() != "Patchsu-Torii" {
		t.Fatalf("s didn't switch to Torii:\n%s", v)
	}
	// The first row below the presets is Gatari: its description shows, and space picks it.
	press(m, "down")
	if v = m.View(); !strings.Contains(v, "─ Gatari ") || !strings.Contains(v, "osu!gatari") || !strings.Contains(v, "Patches: 0101 Gatari connection") {
		t.Fatalf("Gatari's description missing:\n%s", v)
	}
	press(m, " ")
	if v = m.View(); !strings.Contains(v, "(•) Gatari") || !strings.Contains(v, "‹ Custom server ›") || m.buildName() != "Patchsu-Gatari" {
		t.Fatalf("space didn't pick Gatari:\n%s", v)
	}
	// Adding a patch (0009: past Torii, 0001 and 0005) keeps the server: Custom (from Custom server), still Patchsu-Gatari.
	for i := 0; i < 4; i++ {
		press(m, "down")
	}
	press(m, " ")
	if v = m.View(); !strings.Contains(v, "Custom (from Custom server)") || !strings.Contains(v, "[x] 0009") || m.buildName() != "Patchsu-Gatari" {
		t.Fatalf("custom server selection lost its server:\n%s", v)
	}
	m.dataDir = t.TempDir() // skip the data folder question; enter goes on to the check
	press(m, "enter")
	if m.install.Name != "Patchsu-Gatari" || m.cfg.Server != "Gatari" {
		t.Fatalf("install %+v, remembered server %q", m.install, m.cfg.Server)
	}
}

// TestInstallLocation: the install location screen takes a typed folder, refuses someone else's folder, and the
// choice moves the install (and is remembered).
func TestInstallLocation(t *testing.T) {
	src, err := release.FromFS(fstest.MapFS{"patches.meta.json": {Data: []byte(`{"osuCommit":"abc",
 "patches":[{"id":"0001","file":"1.patch","name":"Online restrictions","required":true}],
 "presets":[{"name":"Vanilla+","patches":[]}]}`)}, "1.patch": {Data: []byte("1")}})
	if err != nil {
		t.Fatal(err)
	}
	m := newTestModel(t, src)
	t.Setenv("XDG_DATA_HOME", t.TempDir()) // no real install or menu entry is read
	t.Setenv("XDG_DATA_DIRS", t.TempDir())
	m.opts.Prefix = ""
	m.install = m.locate("Patchsu")
	m.scr = scrCheck
	press(m, "i")
	if m.scr != scrInstallDir {
		t.Fatal("i didn't open the install location")
	}
	if v := m.View(); !strings.Contains(v, "Standard") || !strings.Contains(v, "Next to osu! data") || !strings.Contains(v, "Somewhere else") {
		t.Fatalf("options missing:\n%s", v)
	}
	taken := t.TempDir()
	if err := os.MkdirAll(filepath.Join(taken, "Patchsu"), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(taken, "Patchsu", "notes.txt"), nil, 0o644); err != nil {
		t.Fatal(err)
	}
	press(m, "up") // wraps round to "Somewhere else"
	typeText(m, taken)
	press(m, "enter")
	if v := m.View(); m.scr != scrInstallDir || !strings.Contains(v, "isn't Patchsu") {
		t.Fatalf("someone else's folder was accepted:\n%s", v)
	}
	games := t.TempDir()
	m.instInput.value = nil
	typeText(m, games+"/")
	if v := m.View(); !strings.Contains(v, "Installs into "+filepath.Join(games, "Patchsu")) {
		t.Fatalf("preview missing:\n%s", v)
	}
	press(m, "enter")
	if m.scr != scrCheck || m.cfg.InstallDir != games || m.install.Prefix != filepath.Join(games, "Patchsu") {
		t.Fatalf("screen %v, remembered %q, install %+v", m.scr, m.cfg.InstallDir, m.install)
	}
	if got := m.locate("Patchsu-Gatari").Prefix; got != filepath.Join(games, "Patchsu-Gatari") {
		t.Fatalf("server build not next to it: %s", got)
	}
	// Back to the standard place.
	press(m, "i")
	if m.instCursor != 2 || string(m.instInput.value) != games {
		t.Fatalf("typed folder not restored: row %d, %q", m.instCursor, string(m.instInput.value))
	}
	press(m, "down")
	press(m, "enter")
	if m.cfg.InstallDir != "" || m.install != paths.DefaultInstall(m.info, "Patchsu", "") {
		t.Fatalf("standard not restored: %q %+v", m.cfg.InstallDir, m.install)
	}
}

// TestDataFolder: the data folder screen offers a folder next to osu!'s data and a typed one (which gets its own
// patchsu-data folder, since "delete the data folder" removes it whole).
func TestDataFolder(t *testing.T) {
	src, err := release.FromFS(fstest.MapFS{"patches.meta.json": {Data: []byte(`{"osuCommit":"abc",
 "patches":[{"id":"0001","file":"1.patch","name":"Online restrictions","required":true}],
 "presets":[{"name":"Vanilla+","patches":[]}]}`)}, "1.patch": {Data: []byte("1")}})
	if err != nil {
		t.Fatal(err)
	}
	m := newTestModel(t, src)
	share := t.TempDir()
	t.Setenv("XDG_DATA_HOME", share)
	m.info.OS = "linux"
	m.openDataDir(scrPicker)
	v := m.View()
	if !strings.Contains(v, "Next to osu! data") || !strings.Contains(v, filepath.Join(share, "patchsu-data")) ||
		!strings.Contains(v, "Somewhere else") {
		t.Fatalf("options missing:\n%s", v)
	}
	press(m, "up") // wraps round to "Somewhere else"
	games := t.TempDir()
	typeText(m, games)
	want := filepath.Join(games, "patchsu-data")
	if v := m.View(); !strings.Contains(v, "Uses "+want) {
		t.Fatalf("preview missing:\n%s", v)
	}
	m.Update(tea.KeyMsg{Type: tea.KeyEnter})
	if m.dataDir != want || m.cfg.DataDir != want {
		t.Fatalf("data folder %q, remembered %q", m.dataDir, m.cfg.DataDir)
	}
	// Reopened (f on the check screen), the typed folder is back on its row, and esc returns to the check.
	m.openDataDir(scrCheck)
	if m.dirCursor != 3 || string(m.dirInput.value) != want {
		t.Fatalf("typed folder not restored: row %d %q", m.dirCursor, string(m.dirInput.value))
	}
	press(m, "esc")
	if m.scr != scrCheck {
		t.Fatalf("esc went to %v", m.scr)
	}
}

func TestExpandPath(t *testing.T) {
	t.Setenv("GAMES", "/g")
	for in, want := range map[string]string{
		"~": "/h", "~/Games/": "/h/Games", ` "/x/y" `: "/x/y", "$GAMES/osu": "/g/osu", "%GAMES%/osu": "/g/osu", "": "",
	} {
		if got := expandPath(in, "/h"); got != want {
			t.Errorf("expandPath(%q) = %q, want %q", in, got, want)
		}
	}
}

func typeText(m *model, s string) {
	m.Update(tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune(s)})
}

// newTestModel is the picker after the release loaded. The prefix points nowhere, so no real install is read,
// and the config is saved into a temporary file (the real Patchsu-CLI.json stays untouched).
func newTestModel(t *testing.T, src *release.Source) *model {
	t.Setenv("PATCHSU_CLI_CONFIG", filepath.Join(t.TempDir(), "Patchsu-CLI.json"))
	m := &model{opts: Options{Version: "1.0", Prefix: filepath.Join(os.TempDir(), "patchsu-test-no-install")},
		info: sysinfo.Detect(), cfg: &config.Config{}, width: 100, height: 40}
	m.Update(releaseMsg{src: src})
	return m
}

func press(m *model, s string) {
	var k tea.KeyMsg
	switch s {
	case "down":
		k = tea.KeyMsg{Type: tea.KeyDown}
	case "tab":
		k = tea.KeyMsg{Type: tea.KeyTab}
	case "shift+tab":
		k = tea.KeyMsg{Type: tea.KeyShiftTab}
	case "enter":
		k = tea.KeyMsg{Type: tea.KeyEnter}
	case "up":
		k = tea.KeyMsg{Type: tea.KeyUp}
	case "esc":
		k = tea.KeyMsg{Type: tea.KeyEsc}
	case " ":
		k = tea.KeyMsg{Type: tea.KeySpace, Runes: []rune(" ")}
	default:
		k = tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune(s)}
	}
	m.Update(k)
}
