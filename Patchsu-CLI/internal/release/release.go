// Package release finds a patch release (GitHub, cache or a local folder) and makes its patch files available.
package release

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
)

// Repo is the GitHub repository that publishes patch releases.
const Repo = "PorkingMane/Patchsu"

// Asset names in a patch release.
const (
	ManifestAsset = "manifest.json"
	PatchesAsset  = "patches.zip"
	MetaFile      = "patches.meta.json" // hand-edited source of manifest.json in the repo
	// BundledAtFile sits next to the built-in patches: when they were last changed (RFC 3339), written by go generate.
	BundledAtFile = "bundled-at"
)

// releasesURL lists the repo's releases (a variable so tests can point it at a fake GitHub).
var releasesURL = "https://api.github.com/repos/" + Repo + "/releases?per_page=50"

// ErrNoRelease means the repo has no patch release yet.
var ErrNoRelease = errors.New("no patch release has been published yet at github.com/" + Repo +
	" (use --local DIR to build from a local patches folder)")

// Source is a patch release ready to be used.
type Source struct {
	Manifest *manifest.Manifest
	Tag      string
	// Origin says where it came from: "builtin", "branch" (the repo's main branch), "github" (a patch release),
	// "cache" or "local".
	Origin string
	// Dir holds the .patch files once they're available (always set for "local" and "cache";
	// for "builtin" after Ensure has unpacked them).
	Dir string

	builtin fs.FS // the patches built into the CLI
	// Bundled is when the built-in patches last changed (zero if unknown); Published is when a GitHub release came out.
	Bundled   time.Time
	Published time.Time

	patchesURL  string
	patchesSize int64
	files       map[string]branchFile // "branch" patches not downloaded yet
}

// Describe is a short line for the header, e.g. "release v26" or "local folder".
func (s *Source) Describe() string {
	switch s.Origin {
	case "builtin":
		return "built-in patches"
	case "local":
		return "local patches"
	case "branch":
		return "GitHub patches (" + Branch + "@" + strings.TrimPrefix(s.Tag, branchTag)[:7] + ")"
	case "cache":
		if isBranchTag(s.Tag) {
			return "GitHub patches (" + Branch + "@" + strings.TrimPrefix(s.Tag, branchTag)[:7] + ", offline, cached)"
		}
		return "release " + s.Tag + " (offline, cached)"
	}
	return "release " + s.Tag
}

// PatchPath returns the path of a patch file. Only valid after Ensure.
func (s *Source) PatchPath(id string) string {
	return filepath.Join(s.Dir, s.Manifest.Patch(id).File)
}

// NeedsDownload reports whether Ensure will download patches.zip.
func (s *Source) NeedsDownload() bool { return s.Dir == "" && s.builtin == nil }

// Open picks the patch source: a local folder (local), else the GitHub release tag ("latest" = the newest,
// "main" = the patches on the repo's main branch), else the patches built into the CLI, else the newest GitHub
// release (or the main branch's patches when there's no release). cacheDir may be "".
func Open(ctx context.Context, local, tag string, builtin fs.FS, cacheDir string) (*Source, error) {
	switch {
	case local != "":
		return FromDir(local)
	case tag == "" && builtin != nil:
		return FromFS(builtin)
	case tag == "latest":
		tag = ""
	case tag == Branch:
		s, err := findBranch(ctx, cacheDir, time.Time{})
		if err == nil {
			return s, nil
		}
		if cached, cerr := findCachedBranch(cacheDir); cerr == nil {
			return cached, nil
		}
		return nil, fmt.Errorf("couldn't get the patches from github.com/%s (%v) and none are cached yet", Repo, err)
	}
	return Find(ctx, tag, cacheDir)
}

// FromFS uses the patches built into the CLI (patches.meta.json + *.patch). They're unpacked into the data
// folder by Ensure, at the start of the build. The tag is "builtin-" + a hash of the patches.
func FromFS(fsys fs.FS) (*Source, error) {
	data, err := fs.ReadFile(fsys, MetaFile)
	if err != nil {
		return nil, fmt.Errorf("the built-in patches have no %s", MetaFile)
	}
	m, err := manifest.Parse(data)
	if err != nil {
		return nil, err
	}
	all := sha256.New()
	for i := range m.Patches {
		b, err := fs.ReadFile(fsys, m.Patches[i].File)
		if err != nil {
			return nil, fmt.Errorf("built-in patch %s: %w", m.Patches[i].ID, err)
		}
		sum := sha256.Sum256(b)
		m.Patches[i].SHA256 = hex.EncodeToString(sum[:])
		all.Write(sum[:])
	}
	tag := "builtin-" + hex.EncodeToString(all.Sum(nil))[:12]
	m.Release = tag
	s := &Source{Manifest: m, Tag: tag, Origin: "builtin", builtin: fsys}
	if b, err := fs.ReadFile(fsys, BundledAtFile); err == nil {
		s.Bundled, _ = time.Parse(time.RFC3339, strings.TrimSpace(string(b)))
	}
	return s, nil
}

// UpdateTimeout is how long the update check may take; offline, the built-in patches are used after it.
const UpdateTimeout = 8 * time.Second

// Update looks for GitHub patches that replace the built-in patches b: first the patches on the repo's main branch,
// if they were changed after b was bundled and differ from it; when the branch can't be read, the newest patch
// release under the same rule. It returns nil when there's none, when GitHub can't be reached (the built-in patches
// are fine offline), or when the newer patches need a newer CLI than cliVersion; notice then says so.
func Update(ctx context.Context, b *Source, cacheDir, cliVersion string) (up *Source, notice string) {
	if b == nil || b.Origin != "builtin" {
		return nil, ""
	}
	if br, err := findBranch(ctx, cacheDir, b.Bundled); err == nil {
		if br == nil || sameAsBuiltin(br, b) {
			return nil, ""
		}
		if err := br.Manifest.CheckCLI(cliVersion); err != nil {
			return nil, "The patches on github.com/" + Repo + " changed, but they need a newer Patchsu-CLI (github.com/" +
				Repo + "/releases). Using the patches built into this one."
		}
		return br, "Updated: using the patches from github.com/" + Repo + " (changed " +
			br.Published.Local().Format("2006-01-02 15:04") + "), newer than the ones built into this Patchsu-CLI."
	}
	gh, err := findOnline(ctx, "", cacheDir)
	if err != nil || !gh.Published.After(b.Bundled) || samePatches(gh.Manifest, b.Manifest) {
		return nil, ""
	}
	if err := gh.Manifest.CheckCLI(cliVersion); err != nil {
		return nil, "Patch release " + gh.Tag + " is out, but it needs a newer Patchsu-CLI (github.com/" + Repo +
			"/releases). Using the patches built into this one."
	}
	return gh, "Updated: using patch release " + gh.Tag + " from GitHub, newer than the patches built into this Patchsu-CLI."
}

// samePatches reports whether two manifests have the same patch files (by hash), presets and servers.
func samePatches(a, b *manifest.Manifest) bool {
	if len(a.Patches) != len(b.Patches) || a.OsuCommit != b.OsuCommit {
		return false
	}
	hashes := map[string]string{}
	for _, p := range a.Patches {
		hashes[p.ID] = strings.ToLower(p.SHA256)
	}
	for _, p := range b.Patches {
		if h, ok := hashes[p.ID]; !ok || h != strings.ToLower(p.SHA256) {
			return false
		}
	}
	same := func(x, y any) bool {
		jx, _ := json.Marshal(x)
		jy, _ := json.Marshal(y)
		return string(jx) == string(jy)
	}
	return same(a.Presets, b.Presets) && same(a.Servers, b.Servers)
}

// unpack writes the built-in patches to dir/patches (skipped when an earlier build already did) and points Dir there.
func (s *Source) unpack(cacheDir string) error {
	if cacheDir == "" {
		return errors.New("no data folder for the built-in patches")
	}
	dir := filepath.Join(cacheDir, s.Tag)
	patchDir := filepath.Join(dir, "patches")
	if _, err := os.Stat(filepath.Join(dir, ".complete")); err == nil {
		s.Dir = patchDir
		if s.verify() == nil {
			return nil
		}
	}
	s.Dir = ""
	if err := os.RemoveAll(dir); err != nil {
		return err
	}
	if err := os.MkdirAll(patchDir, 0o755); err != nil {
		return err
	}
	for _, p := range s.Manifest.Patches {
		b, err := fs.ReadFile(s.builtin, p.File)
		if err != nil {
			return err
		}
		if err := os.WriteFile(filepath.Join(patchDir, filepath.Base(p.File)), b, 0o644); err != nil {
			return err
		}
	}
	s.Dir = patchDir
	if err := s.verify(); err != nil {
		s.Dir = ""
		return err
	}
	data, err := s.Manifest.Marshal()
	if err != nil {
		return err
	}
	if err := os.WriteFile(filepath.Join(dir, ManifestAsset), data, 0o644); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, ".complete"), nil, 0o644)
}

// DownloadSize is the size of patches.zip, if known.
func (s *Source) DownloadSize() int64 { return s.patchesSize }

// FromDir uses a local folder of patches. It reads manifest.json, or else patches.meta.json
// (the file the release manifest is generated from) and fills in the hashes itself.
func FromDir(dir string) (*Source, error) {
	abs, err := filepath.Abs(dir)
	if err != nil {
		return nil, err
	}
	var m *manifest.Manifest
	if _, err := os.Stat(filepath.Join(abs, ManifestAsset)); err == nil {
		m, err = manifest.Load(filepath.Join(abs, ManifestAsset))
		if err != nil {
			return nil, err
		}
	} else {
		m, err = BuildManifest(abs, "local")
		if err != nil {
			return nil, err
		}
	}
	s := &Source{Manifest: m, Tag: m.Release, Origin: "local", Dir: abs}
	return s, s.verify()
}

// BuildManifest makes a manifest from dir/patches.meta.json plus the SHA-256 of every patch file in dir.
func BuildManifest(dir, tag string) (*manifest.Manifest, error) {
	data, err := os.ReadFile(filepath.Join(dir, MetaFile))
	if err != nil {
		return nil, fmt.Errorf("%s has neither %s nor %s", dir, ManifestAsset, MetaFile)
	}
	m, err := manifest.Parse(data)
	if err != nil {
		return nil, err
	}
	m.Release = tag
	for i := range m.Patches {
		sum, err := download.SHA256File(filepath.Join(dir, m.Patches[i].File))
		if err != nil {
			return nil, fmt.Errorf("patch %s: %w", m.Patches[i].ID, err)
		}
		m.Patches[i].SHA256 = sum
	}
	return m, nil
}

// verify checks that every patch file exists and matches its hash.
func (s *Source) verify() error {
	for _, p := range s.Manifest.Patches {
		path := filepath.Join(s.Dir, p.File)
		sum, err := download.SHA256File(path)
		if err != nil {
			return fmt.Errorf("patch %s: %w", p.ID, err)
		}
		if p.SHA256 != "" && !strings.EqualFold(sum, p.SHA256) {
			return fmt.Errorf("patch %s (%s) doesn't match the checksum in the manifest", p.ID, p.File)
		}
	}
	return nil
}

type ghAsset struct {
	Name string `json:"name"`
	URL  string `json:"browser_download_url"`
	Size int64  `json:"size"`
}

type ghRelease struct {
	Tag        string    `json:"tag_name"`
	Draft      bool      `json:"draft"`
	Prerelease bool      `json:"prerelease"`
	Published  time.Time `json:"published_at"`
	Assets     []ghAsset `json:"assets"`
}

func (r ghRelease) asset(name string) *ghAsset {
	for i := range r.Assets {
		if r.Assets[i].Name == name {
			return &r.Assets[i]
		}
	}
	return nil
}

// isPatchRelease: CLI releases are tagged cli-*, everything else with both assets is a patch release.
func (r ghRelease) isPatchRelease() bool {
	return !r.Draft && !strings.HasPrefix(r.Tag, "cli-") && r.asset(ManifestAsset) != nil && r.asset(PatchesAsset) != nil
}

var apiHeaders = map[string]string{"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"}

// Find looks up a patch release on GitHub: tag "" means the newest one.
// If GitHub can't be reached, it falls back to the newest (or the named) release cached in cacheDir.
// cacheDir may be "" when no data folder is chosen yet.
func Find(ctx context.Context, tag, cacheDir string) (*Source, error) {
	src, err := findOnline(ctx, tag, cacheDir)
	if errors.Is(err, ErrNoRelease) && tag == "" {
		if br, berr := findBranch(ctx, cacheDir, time.Time{}); berr == nil {
			return br, nil
		}
	}
	if err == nil || errors.Is(err, ErrNoRelease) {
		return src, err
	}
	if cached, cerr := findCached(tag, cacheDir); cerr == nil {
		return cached, nil
	}
	return nil, fmt.Errorf("couldn't reach GitHub (%v) and no release is cached yet", err)
}

func findOnline(ctx context.Context, tag, cacheDir string) (*Source, error) {
	data, err := download.Bytes(ctx, releasesURL, apiHeaders)
	var se *download.StatusError
	if errors.As(err, &se) && se.Code == 404 {
		return nil, ErrNoRelease
	}
	if err != nil {
		return nil, err
	}
	var releases []ghRelease
	if err := json.Unmarshal(data, &releases); err != nil {
		return nil, fmt.Errorf("unexpected answer from GitHub: %w", err)
	}
	var pick *ghRelease
	for i := range releases { // GitHub lists the newest first
		r := releases[i]
		if !r.isPatchRelease() || (tag == "" && r.Prerelease) {
			continue
		}
		if tag == "" || r.Tag == tag {
			pick = &releases[i]
			break
		}
	}
	if pick == nil {
		if tag != "" {
			return nil, fmt.Errorf("release %q not found at github.com/%s", tag, Repo)
		}
		return nil, ErrNoRelease
	}

	// A release already downloaded completely is used from the cache.
	if cacheDir != "" {
		if s, err := loadCached(filepath.Join(cacheDir, pick.Tag)); err == nil {
			s.Origin, s.Published = "github", pick.Published
			return s, nil
		}
	}
	mdata, err := download.Bytes(ctx, pick.asset(ManifestAsset).URL, nil)
	if err != nil {
		return nil, err
	}
	m, err := manifest.Parse(mdata)
	if err != nil {
		return nil, err
	}
	if m.Release == "" {
		m.Release = pick.Tag
	}
	pa := pick.asset(PatchesAsset)
	return &Source{Manifest: m, Tag: pick.Tag, Origin: "github", Published: pick.Published, patchesURL: pa.URL, patchesSize: pa.Size}, nil
}

// cached release layout: <cacheDir>/<tag>/manifest.json + <cacheDir>/<tag>/patches/*.patch + .complete marker
func loadCached(dir string) (*Source, error) {
	if _, err := os.Stat(filepath.Join(dir, ".complete")); err != nil {
		return nil, err
	}
	m, err := manifest.Load(filepath.Join(dir, ManifestAsset))
	if err != nil {
		return nil, err
	}
	s := &Source{Manifest: m, Tag: filepath.Base(dir), Origin: "cache", Dir: filepath.Join(dir, "patches")}
	return s, s.verify()
}

// findCachedBranch returns the newest cached main-branch patches.
func findCachedBranch(cacheDir string) (*Source, error) {
	return findCachedMatching(cacheDir, isBranchTag)
}

func findCached(tag, cacheDir string) (*Source, error) {
	if cacheDir == "" {
		return nil, os.ErrNotExist
	}
	if tag != "" {
		return loadCached(filepath.Join(cacheDir, tag))
	}
	return findCachedMatching(cacheDir, func(string) bool { return true })
}

// findCachedMatching returns the most recently cached release whose tag passes keep.
func findCachedMatching(cacheDir string, keep func(tag string) bool) (*Source, error) {
	if cacheDir == "" {
		return nil, os.ErrNotExist
	}
	entries, err := os.ReadDir(cacheDir)
	if err != nil {
		return nil, err
	}
	type cand struct {
		s   *Source
		mod int64
	}
	var cands []cand
	for _, e := range entries {
		if !e.IsDir() || !keep(e.Name()) {
			continue
		}
		s, err := loadCached(filepath.Join(cacheDir, e.Name()))
		if err != nil {
			continue
		}
		info, _ := os.Stat(filepath.Join(cacheDir, e.Name(), ".complete"))
		cands = append(cands, cand{s, info.ModTime().UnixNano()})
	}
	if len(cands) == 0 {
		return nil, os.ErrNotExist
	}
	sort.Slice(cands, func(i, j int) bool { return cands[i].mod > cands[j].mod })
	return cands[0].s, nil
}

// Ensure downloads and unpacks patches.zip into cacheDir/<tag> if needed, and checks every patch's hash.
func (s *Source) Ensure(ctx context.Context, cacheDir string, progress download.Progress) error {
	if s.Dir != "" {
		return s.verify()
	}
	if s.builtin != nil {
		return s.unpack(cacheDir)
	}
	if s.files != nil {
		return s.ensureBranch(ctx, cacheDir, progress)
	}
	dir := filepath.Join(cacheDir, s.Tag)
	if err := os.RemoveAll(dir); err != nil {
		return err
	}
	zipPath := filepath.Join(dir, PatchesAsset)
	if err := download.File(ctx, s.patchesURL, zipPath, download.Hash{}, progress); err != nil {
		return err
	}
	patchDir := filepath.Join(dir, "patches")
	if err := download.Unzip(zipPath, patchDir, true); err != nil {
		return fmt.Errorf("unpacking %s: %w", PatchesAsset, err)
	}
	os.Remove(zipPath)
	s.Dir = patchDir
	if err := s.verify(); err != nil {
		s.Dir = ""
		return err
	}
	data, err := s.Manifest.Marshal()
	if err != nil {
		return err
	}
	if err := os.WriteFile(filepath.Join(dir, ManifestAsset), data, 0o644); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, ".complete"), nil, 0o644)
}
