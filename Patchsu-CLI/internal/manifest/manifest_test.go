package manifest

import (
	"os"
	"reflect"
	"strings"
	"testing"
)

// The real patch metadata from the Patchsu repo, when it's next to this checkout.
const metaPath = "../../../Patchsu/patches/patches.meta.json"

func load(t *testing.T) *Manifest {
	t.Helper()
	data, err := os.ReadFile(metaPath)
	if err != nil {
		t.Skip("patches.meta.json not found:", err)
	}
	m, err := Parse(data)
	if err != nil {
		t.Fatal(err)
	}
	return m
}

func TestEnablePullsInNeeds(t *testing.T) {
	m := load(t)
	s := m.NewSelection()
	added := m.Enable(s, "0009")
	if want := []string{"0002"}; !reflect.DeepEqual(added, want) {
		t.Fatalf("added %v, want %v", added, want)
	}
	if got := m.IDs(s); !reflect.DeepEqual(got, []string{"0001", "0002", "0009"}) {
		t.Fatalf("selection %v", got)
	}
}

func TestDisableRemovesDependents(t *testing.T) {
	m := load(t)
	s := m.PresetSelection(m.Preset("Local play"))
	removed := m.Disable(s, "0005")
	if len(removed) != 0 {
		t.Fatalf("removed %v, want nothing (0500 / 0501 don't need 0005)", removed)
	}
	removed = m.Disable(s, "0002")
	if want := []string{"0004", "0009"}; !reflect.DeepEqual(removed, want) {
		t.Fatalf("removed %v, want %v", removed, want)
	}
	if m.Disable(s, "0001") != nil || !s["0001"] {
		t.Fatal("0001 must stay selected")
	}
}

func TestMissing(t *testing.T) {
	m := load(t)
	s := m.NewSelection()
	if got := m.Missing(s, "0004"); !reflect.DeepEqual(got, []string{"0002"}) {
		t.Fatalf("missing %v", got)
	}
	if got := m.Missing(s, "0003"); len(got) != 0 {
		t.Fatalf("0003 should need nothing beyond 0001, got %v", got)
	}
}

func TestPresets(t *testing.T) {
	m := load(t)
	want := map[string]string{
		"Vanilla+": "0001 0003 0003b 0005 0006 0007 0010",
	}
	var all []string
	for _, p := range m.ListPatches() {
		all = append(all, p.ID)
	}
	want["Local play"] = strings.Join(all, " ") // every patch, whatever the release contains
	if p := m.Preset("Custom server"); p == nil || !p.Server {
		t.Fatal(`"Custom server" preset missing or not a server preset`)
	} else if got := strings.Join(m.IDs(m.PresetSelection(p)), " "); got != want["Vanilla+"] {
		t.Errorf("Custom server = %s, want Vanilla+ plus the server's patches", got)
	}
	if p := m.Preset("Custom server + local play"); p == nil || !p.Server {
		t.Fatal(`"Custom server + local play" preset missing or not a server preset`)
	} else if got := strings.Join(m.IDs(m.PresetSelection(p)), " "); got != want["Local play"] {
		t.Errorf("Custom server + local play = %s, want every patch plus the server's patches", got)
	}
	for name, ids := range want {
		p := m.Preset(name)
		if p == nil {
			t.Fatalf("preset %q missing", name)
		}
		s := m.PresetSelection(p)
		if got := strings.Join(m.IDs(s), " "); got != ids {
			t.Errorf("%s = %s, want %s", name, got, ids)
		}
		if mp := m.MatchPreset(s); mp == nil || mp.Name != name {
			t.Errorf("MatchPreset(%s) = %v", name, mp)
		}
	}
}

func TestResolve(t *testing.T) {
	m := load(t)
	s, added, err := m.Resolve([]string{"0003", "0009"})
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(added, []string{"0002"}) {
		t.Fatalf("added %v", added)
	}
	if !s["0001"] {
		t.Fatal("0001 missing")
	}
	if _, _, err := m.Resolve([]string{"9999"}); err == nil {
		t.Fatal("unknown id accepted")
	}
}

func TestValidation(t *testing.T) {
	bad := []string{
		`{"osuCommit":"x","patches":[{"id":"a","file":"a","needs":["b"]},{"id":"b","file":"b","needs":["a"]}]}`,
		`{"osuCommit":"x","patches":[{"id":"a","file":"a","needs":["zz"]}]}`,
		`{"osuCommit":"x","patches":[{"id":"a","file":"a"}],"presets":[{"name":"p","patches":["zz"]}]}`,
		`{"osuCommit":"x","patches":[{"id":"a","file":"a"},{"id":"a","file":"b"}]}`,
	}
	for _, b := range bad {
		if _, err := Parse([]byte(b)); err == nil {
			t.Errorf("accepted %s", b)
		}
	}
}

func TestCheckCLI(t *testing.T) {
	m := &Manifest{Release: "v1", MinCLI: "1.2.0"}
	for v, ok := range map[string]bool{"1.2.0": true, "1.10.0": true, "1.1.9": false, "0.9": false, "0.1.0-dev": true} {
		if (m.CheckCLI(v) == nil) != ok {
			t.Errorf("CheckCLI(%s) ok=%v", v, !ok)
		}
	}
}

// serverManifest is a release with two custom servers, like a future one.
const serverManifest = `{"osuCommit":"x",
 "patches":[
  {"id":"0001","file":"1","required":true},
  {"id":"0002","file":"2","needs":["0001"]},
  {"id":"0003","file":"3","needs":["0001"]},
  {"id":"1000","file":"g","needs":["0001"]},
  {"id":"1001","file":"g2","needs":["1000"]},
  {"id":"1010","file":"t","needs":["0001"]}],
 "presets":[
  {"name":"Vanilla+","patches":["0003"]},
  {"name":"Local play","patches":"*"},
  {"name":"Custom server","patches":["0003"],"server":true}],
 "servers":[
  {"name":"Gatari","patches":["1000","1001"]},
  {"name":"Torii","patches":["1010"]}]}`

func TestServers(t *testing.T) {
	m, err := Parse([]byte(serverManifest))
	if err != nil {
		t.Fatal(err)
	}
	ids := func(s Selection) string { return strings.Join(m.IDs(s), " ") }
	var list []string
	for _, p := range m.ListPatches() {
		list = append(list, p.ID)
	}
	if got := strings.Join(list, " "); got != "0001 0002 0003" {
		t.Fatalf("list %s: server patches must be hidden", got)
	}
	if got := ids(m.PresetSelection(m.Preset("Local play"))); got != "0001 0002 0003" {
		t.Fatalf(`"*" = %s, must not include server patches`, got)
	}
	cs := m.Preset("custom server")
	g := m.PresetSelectionFor(cs, m.Server("gatari"))
	if got := ids(g); got != "0001 0003 1000 1001" {
		t.Fatalf("Custom server + Gatari = %s", got)
	}
	if m.BuildName(g) != "Patchsu-Gatari" || m.BuildName(m.NewSelection()) != "Patchsu" {
		t.Fatalf("build names %q / %q", m.BuildName(g), m.BuildName(m.NewSelection()))
	}
	if p := m.MatchPreset(g); p != cs {
		t.Fatalf("MatchPreset(server build) = %v", p)
	}
	if p := m.MatchPreset(m.PresetSelection(cs)); p == cs {
		t.Fatal("a server preset without a server must not match")
	}
	m.SetServer(g, m.Server("Torii"))
	if got := ids(g); got != "0001 0003 1010" || m.BuildName(g) != "Patchsu-Torii" {
		t.Fatalf("switch to Torii = %s (%s)", got, m.BuildName(g))
	}
	m.SetServer(g, nil)
	if got := ids(g); got != "0001 0003" {
		t.Fatalf("no server = %s", got)
	}
	if _, _, err := m.Resolve([]string{"1000", "1010"}); err == nil {
		t.Fatal("two servers in one build accepted")
	}
	if s, _, err := m.Resolve([]string{"1001"}); err != nil || m.BuildName(s) != "Patchsu-Gatari" {
		t.Fatalf("Resolve(0102) = %v, %v", s, err)
	}
}

func TestServerValidation(t *testing.T) {
	base := `{"osuCommit":"x","patches":[{"id":"0001","file":"1","required":true},{"id":"a","file":"a","needs":["0001"]},` +
		`{"id":"b","file":"b","needs":["0001"]}]`
	bad := []string{
		base + `,"servers":[{"name":"Bad name","patches":["a"]}]}`,
		base + `,"servers":[{"name":"S","patches":["zz"]}]}`,
		base + `,"servers":[{"name":"S","patches":["0001"]}]}`,
		base + `,"servers":[{"name":"S","patches":["a"]},{"name":"s","patches":["b"]}]}`,
		base + `,"servers":[{"name":"S","patches":["a"]},{"name":"T","patches":["a"]}]}`,
		base + `,"servers":[{"name":"S","patches":[]}]}`,
		base + `,"servers":[{"name":"S","patches":["a"]}],"presets":[{"name":"p","patches":["a"]}]}`,
		`{"osuCommit":"x","patches":[{"id":"a","file":"a"},{"id":"b","file":"b","needs":["a"]}],"servers":[{"name":"S","patches":["a"]}]}`,
	}
	for _, b := range bad {
		if _, err := Parse([]byte(b)); err == nil {
			t.Errorf("accepted %s", b)
		}
	}
	if _, err := Parse([]byte(base + `,"servers":[{"name":"Gatari","patches":["a"]}]}`)); err != nil {
		t.Fatal(err)
	}
}
