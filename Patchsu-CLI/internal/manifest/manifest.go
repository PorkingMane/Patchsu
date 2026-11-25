// Package manifest describes a patch release: which patches exist, what each one needs, and the presets.
package manifest

import (
	"encoding/json"
	"fmt"
	"os"
	"regexp"
	"sort"
	"strings"
)

// Manifest is the contents of manifest.json in a patch release.
type Manifest struct {
	Release   string   `json:"release"`
	MinCLI    string   `json:"minCli,omitempty"`
	OsuCommit string   `json:"osuCommit"`
	OsuRepo   string   `json:"osuRepo,omitempty"` // defaults to https://github.com/ppy/osu
	DotnetSDK string   `json:"dotnetSdk"`         // major.minor channel, e.g. "10.0"
	Patches   []Patch  `json:"patches"`
	Presets   []Preset `json:"presets"`
	// Servers are custom osu! servers with patches that connect the game to them. Their patches are hidden from the
	// patch list and only come with the "Custom server" preset; the build is then named Patchsu-<Name>.
	Servers []Server `json:"servers,omitempty"`

	serverOf map[string]*Server // patch id → server it belongs to
}

// Server is a custom osu! server and the patches that connect the game to it.
type Server struct {
	Name        string   `json:"name"` // also the build name suffix: Patchsu-<Name>
	Description string   `json:"description"`
	Patches     []string `json:"patches"`
}

// BaseName is the name of a build without a server.
const BaseName = "Patchsu"

// BuildName is "Patchsu", or "Patchsu-<Name>" for a server build.
func (s *Server) BuildName() string {
	if s == nil {
		return BaseName
	}
	return BaseName + "-" + s.Name
}

// serverName: a server name becomes part of file and folder names, so it's kept simple.
var serverName = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9_.-]{0,31}$`)

// ValidServerName reports whether name can be used as a server name (Patchsu-<name>).
func ValidServerName(name string) bool {
	return serverName.MatchString(name) && !strings.HasSuffix(name, ".")
}

// Patch is one .patch file.
type Patch struct {
	ID          string   `json:"id"`
	File        string   `json:"file"`
	Name        string   `json:"name"`
	Description string   `json:"description"`
	Needs       []string `json:"needs"`
	Required    bool     `json:"required,omitempty"`
	Note        string   `json:"note,omitempty"` // short hint shown in the list, e.g. "big, slower build"
	SHA256      string   `json:"sha256,omitempty"`
}

// Preset is a named selection. Patches is either a list of ids or the string "*" (every patch).
type Preset struct {
	Name        string          `json:"name"`
	Description string          `json:"description"`
	RawPatches  json.RawMessage `json:"patches"`
	// Server presets add the patches of one server (chosen separately) to their own patches.
	Server bool `json:"server,omitempty"`

	patches []string
	all     bool
}

// DefaultOsuRepo is used when the manifest doesn't name one.
const DefaultOsuRepo = "https://github.com/ppy/osu"

// Parse reads and validates a manifest.
func Parse(data []byte) (*Manifest, error) {
	var m Manifest
	if err := json.Unmarshal(data, &m); err != nil {
		return nil, fmt.Errorf("manifest.json is not valid JSON: %w", err)
	}
	if err := m.validate(); err != nil {
		return nil, err
	}
	return &m, nil
}

// Load reads a manifest from a file.
func Load(path string) (*Manifest, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	return Parse(data)
}

// Marshal writes the manifest as indented JSON.
func (m *Manifest) Marshal() ([]byte, error) {
	return json.MarshalIndent(m, "", "  ")
}

func (m *Manifest) validate() error {
	if m.OsuCommit == "" {
		return fmt.Errorf("manifest: osuCommit is missing")
	}
	if m.OsuRepo == "" {
		m.OsuRepo = DefaultOsuRepo
	}
	if m.DotnetSDK == "" {
		m.DotnetSDK = "10.0"
	}
	if len(m.Patches) == 0 {
		return fmt.Errorf("manifest: no patches")
	}
	seen := map[string]bool{}
	for _, p := range m.Patches {
		if p.ID == "" || p.File == "" {
			return fmt.Errorf("manifest: a patch has no id or file")
		}
		if seen[p.ID] {
			return fmt.Errorf("manifest: patch %s is listed twice", p.ID)
		}
		seen[p.ID] = true
	}
	for _, p := range m.Patches {
		for _, n := range p.Needs {
			if !seen[n] {
				return fmt.Errorf("manifest: patch %s needs unknown patch %s", p.ID, n)
			}
		}
	}
	if err := m.checkCycles(); err != nil {
		return err
	}
	m.serverOf = map[string]*Server{}
	names := map[string]bool{}
	for i := range m.Servers {
		sv := &m.Servers[i]
		if !ValidServerName(sv.Name) {
			return fmt.Errorf("manifest: server name %q must be 1–32 letters, digits, '-', '_' or '.'", sv.Name)
		}
		if names[strings.ToLower(sv.Name)] {
			return fmt.Errorf("manifest: server %s is listed twice", sv.Name)
		}
		names[strings.ToLower(sv.Name)] = true
		if len(sv.Patches) == 0 {
			return fmt.Errorf("manifest: server %s has no patches", sv.Name)
		}
		for _, id := range sv.Patches {
			p := m.Patch(id)
			switch {
			case p == nil:
				return fmt.Errorf("manifest: server %s names unknown patch %s", sv.Name, id)
			case p.Required:
				return fmt.Errorf("manifest: server %s: patch %s is required by every build, it can't belong to a server", sv.Name, id)
			case m.serverOf[id] != nil:
				return fmt.Errorf("manifest: patch %s belongs to servers %s and %s", id, m.serverOf[id].Name, sv.Name)
			}
			m.serverOf[id] = sv
		}
	}
	for _, p := range m.Patches {
		if m.serverOf[p.ID] != nil {
			continue
		}
		for _, n := range p.Needs {
			if sv := m.serverOf[n]; sv != nil {
				return fmt.Errorf("manifest: patch %s needs %s, which only comes with server %s", p.ID, n, sv.Name)
			}
		}
	}
	for i := range m.Presets {
		pr := &m.Presets[i]
		var s string
		if json.Unmarshal(pr.RawPatches, &s) == nil {
			if s != "*" {
				return fmt.Errorf("manifest: preset %q: patches must be a list or \"*\"", pr.Name)
			}
			pr.all = true
			continue
		}
		if err := json.Unmarshal(pr.RawPatches, &pr.patches); err != nil {
			return fmt.Errorf("manifest: preset %q: patches must be a list or \"*\"", pr.Name)
		}
		for _, id := range pr.patches {
			if !seen[id] {
				return fmt.Errorf("manifest: preset %q names unknown patch %s", pr.Name, id)
			}
			if sv := m.serverOf[id]; sv != nil {
				return fmt.Errorf("manifest: preset %q names %s, which only comes with server %s", pr.Name, id, sv.Name)
			}
		}
	}
	return nil
}

func (m *Manifest) checkCycles() error {
	const (
		unvisited = iota
		visiting
		done
	)
	state := map[string]int{}
	var visit func(id string, path []string) error
	visit = func(id string, path []string) error {
		switch state[id] {
		case visiting:
			return fmt.Errorf("manifest: patches need each other in a loop: %s", strings.Join(append(path, id), " → "))
		case done:
			return nil
		}
		state[id] = visiting
		for _, n := range m.Patch(id).Needs {
			if err := visit(n, append(path, id)); err != nil {
				return err
			}
		}
		state[id] = done
		return nil
	}
	for _, p := range m.Patches {
		if err := visit(p.ID, nil); err != nil {
			return err
		}
	}
	return nil
}

// Patch returns the patch with this id, or nil.
func (m *Manifest) Patch(id string) *Patch {
	for i := range m.Patches {
		if m.Patches[i].ID == id {
			return &m.Patches[i]
		}
	}
	return nil
}

// ServerPatch returns the server a patch belongs to, or nil for an ordinary patch.
func (m *Manifest) ServerPatch(id string) *Server { return m.serverOf[id] }

// ListPatches returns the ordinary patches (not a server's), in manifest order: the rows of the patch list.
func (m *Manifest) ListPatches() []*Patch {
	var out []*Patch
	for i := range m.Patches {
		if m.serverOf[m.Patches[i].ID] == nil {
			out = append(out, &m.Patches[i])
		}
	}
	return out
}

// Server returns the server with this name (case-insensitive), or nil.
func (m *Manifest) Server(name string) *Server {
	for i := range m.Servers {
		if strings.EqualFold(m.Servers[i].Name, name) {
			return &m.Servers[i]
		}
	}
	return nil
}

// ServerOf returns the server whose patches are in the selection, or nil for a plain Patchsu build.
func (m *Manifest) ServerOf(s Selection) *Server {
	for _, id := range m.IDs(s) {
		if sv := m.serverOf[id]; sv != nil {
			return sv
		}
	}
	return nil
}

// BuildName is the name of the game the selection builds: "Patchsu" or "Patchsu-<Server>".
func (m *Manifest) BuildName(s Selection) string { return m.ServerOf(s).BuildName() }

// SetServer replaces the server patches in s with those of sv (nil: none). Patches that needed the old server's
// patches are deselected with them.
func (m *Manifest) SetServer(s Selection, sv *Server) {
	for _, id := range m.IDs(s) {
		if m.serverOf[id] != nil && m.serverOf[id] != sv {
			m.Disable(s, id)
		}
	}
	if sv != nil {
		for _, id := range sv.Patches {
			m.Enable(s, id)
		}
	}
}

// AllSelection selects every ordinary patch (no server's).
func (m *Manifest) AllSelection() Selection {
	s := m.NewSelection()
	for _, p := range m.ListPatches() {
		s[p.ID] = true
	}
	return s
}

// Preset returns the preset with this name (case-insensitive), or nil.
func (m *Manifest) Preset(name string) *Preset {
	for i := range m.Presets {
		if strings.EqualFold(m.Presets[i].Name, name) {
			return &m.Presets[i]
		}
	}
	return nil
}

// Selection is a set of selected patch ids.
type Selection map[string]bool

// Clone copies the selection.
func (s Selection) Clone() Selection {
	c := make(Selection, len(s))
	for k, v := range s {
		if v {
			c[k] = true
		}
	}
	return c
}

// Equal reports whether both selections contain the same patches.
func (s Selection) Equal(o Selection) bool {
	return len(s.Clone()) == len(o.Clone()) && s.contains(o)
}

func (s Selection) contains(o Selection) bool {
	for k, v := range o {
		if v && !s[k] {
			return false
		}
	}
	return true
}

// IDs returns the selected ids in manifest order (the order they are applied in).
func (m *Manifest) IDs(s Selection) []string {
	var ids []string
	for _, p := range m.Patches {
		if s[p.ID] {
			ids = append(ids, p.ID)
		}
	}
	return ids
}

// NewSelection returns a selection with only the required patches.
func (m *Manifest) NewSelection() Selection {
	s := Selection{}
	for _, p := range m.Patches {
		if p.Required {
			s[p.ID] = true
		}
	}
	return s
}

// Enable selects a patch and everything it needs, recursively.
// It returns the ids that were selected in addition to id, in manifest order.
func (m *Manifest) Enable(s Selection, id string) []string {
	added := Selection{}
	var walk func(string)
	walk = func(cur string) {
		if s[cur] {
			return
		}
		s[cur] = true
		if cur != id {
			added[cur] = true
		}
		for _, n := range m.Patch(cur).Needs {
			walk(n)
		}
	}
	walk(id)
	return m.IDs(added)
}

// Disable deselects a patch and every selected patch that needs it, directly or indirectly.
// Required patches can't be disabled. It returns the ids deselected in addition to id, in manifest order.
func (m *Manifest) Disable(s Selection, id string) []string {
	if p := m.Patch(id); p == nil || p.Required || !s[id] {
		return nil
	}
	delete(s, id)
	removed := Selection{}
	for changed := true; changed; {
		changed = false
		for _, p := range m.Patches {
			if !s[p.ID] {
				continue
			}
			for _, n := range p.Needs {
				if !s[n] {
					delete(s, p.ID)
					removed[p.ID] = true
					changed = true
					break
				}
			}
		}
	}
	return m.IDs(removed)
}

// Missing returns the patches this patch needs (directly or indirectly) that aren't selected, in manifest order.
func (m *Manifest) Missing(s Selection, id string) []string {
	missing := Selection{}
	var walk func(string)
	walk = func(cur string) {
		for _, n := range m.Patch(cur).Needs {
			if !s[n] && !missing[n] {
				missing[n] = true
				walk(n)
			}
		}
	}
	walk(id)
	return m.IDs(missing)
}

// DirectMissing returns only the direct requirements of id that aren't selected.
func (m *Manifest) DirectMissing(s Selection, id string) []string {
	var out []string
	for _, n := range m.Patch(id).Needs {
		if !s[n] {
			out = append(out, n)
		}
	}
	return out
}

// PresetSelection returns the full selection of a preset: its patches, everything they need, and the required ones.
// "*" means every ordinary patch. A server preset's server patches are added with PresetSelectionFor.
func (m *Manifest) PresetSelection(p *Preset) Selection {
	if p.all {
		return m.AllSelection()
	}
	s := m.NewSelection()
	for _, id := range p.patches {
		m.Enable(s, id)
	}
	return s
}

// PresetSelectionFor is PresetSelection plus the patches of sv when p is a server preset.
func (m *Manifest) PresetSelectionFor(p *Preset, sv *Server) Selection {
	s := m.PresetSelection(p)
	if p.Server && sv != nil {
		m.SetServer(s, sv)
	}
	return s
}

// MatchPreset returns the preset whose selection equals s, or nil. A selection with a server's patches only
// matches server presets, and one without only the others.
func (m *Manifest) MatchPreset(s Selection) *Preset {
	sv := m.ServerOf(s)
	for i := range m.Presets {
		p := &m.Presets[i]
		if p.Server != (sv != nil) {
			continue
		}
		if m.PresetSelectionFor(p, sv).Equal(s) {
			return p
		}
	}
	return nil
}

// Resolve turns a list of ids (e.g. from --patches) into a full selection.
// It returns the selection and the ids that were added because they were needed.
func (m *Manifest) Resolve(ids []string) (Selection, []string, error) {
	s := m.NewSelection()
	asked := Selection{}
	for _, raw := range ids {
		id := strings.TrimSpace(raw)
		if id == "" {
			continue
		}
		if m.Patch(id) == nil {
			return nil, nil, fmt.Errorf("unknown patch %q (see `Patchsu-CLI list`)", id)
		}
		asked[id] = true
	}
	for _, p := range m.Patches {
		if asked[p.ID] {
			m.Enable(s, p.ID)
		}
	}
	var servers []string
	for i := range m.Servers {
		for _, id := range m.Servers[i].Patches {
			if s[id] {
				servers = append(servers, m.Servers[i].Name)
				break
			}
		}
	}
	if len(servers) > 1 {
		return nil, nil, fmt.Errorf("a build can connect to one server only, not %s", strings.Join(servers, " and "))
	}
	var added []string
	for _, id := range m.IDs(s) {
		if !asked[id] && !m.Patch(id).Required {
			added = append(added, id)
		}
	}
	return s, added, nil
}

// Label is "0007 Replay mod toggles".
func (p *Patch) Label() string { return p.ID + " " + p.Name }

// Labels returns "id name" for each id, joined with ", ".
func (m *Manifest) Labels(ids []string) string {
	out := make([]string, len(ids))
	for i, id := range ids {
		out[i] = m.Patch(id).Label()
	}
	return strings.Join(out, ", ")
}

// SortedIDs returns the keys of s sorted, for stable output.
func SortedIDs(s Selection) []string {
	var ids []string
	for k, v := range s {
		if v {
			ids = append(ids, k)
		}
	}
	sort.Strings(ids)
	return ids
}

// CheckCLI returns an error when this release needs a newer Patchsu-CLI than version.
// Development builds (version ending in "-dev") are never refused.
func (m *Manifest) CheckCLI(version string) error {
	if m.MinCLI == "" || strings.HasSuffix(version, "-dev") {
		return nil
	}
	if compareVersions(version, m.MinCLI) < 0 {
		return fmt.Errorf("patch release %s needs Patchsu-CLI %s or newer (this is %s). Download the newest Patchsu-CLI from github.com/PorkingMane/Patchsu/releases",
			m.Release, m.MinCLI, version)
	}
	return nil
}

func compareVersions(a, b string) int {
	pa := strings.FieldsFunc(strings.TrimPrefix(a, "v"), func(r rune) bool { return r == '.' || r == '-' })
	pb := strings.FieldsFunc(strings.TrimPrefix(b, "v"), func(r rune) bool { return r == '.' || r == '-' })
	for i := 0; i < len(pa) || i < len(pb); i++ {
		var x, y int
		if i < len(pa) {
			fmt.Sscanf(pa[i], "%d", &x)
		}
		if i < len(pb) {
			fmt.Sscanf(pb[i], "%d", &y)
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
