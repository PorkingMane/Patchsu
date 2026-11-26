package release

import (
	"context"
	"crypto/sha1"
	"encoding/hex"
	"encoding/json"
	"encoding/xml"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
)

// The patches as they are on the repo's main branch: what the CLI uses when they changed after it was built,
// so a patch fix pushed to the repo reaches users without a new CLI or a patch release.
const (
	Branch      = "main"
	PatchesPath = "Patchsu/patches"
	branchTag   = Branch + "-" // cache folder / tag prefix: "main-<commit>"
)

// feedURL, apiURL and rawURL are variables so tests can point them at a fake GitHub. The commit feed and raw files
// don't count against GitHub's API limit (60 requests an hour per IP without a token), so the API is only needed
// for the file list of a new commit, and even that has a fallback.
var (
	feedURL = "https://github.com/" + Repo + "/commits/" + Branch + "/" + PatchesPath + ".atom"
	apiURL  = "https://api.github.com/repos/" + Repo
	rawURL  = "https://raw.githubusercontent.com/" + Repo
)

// branchFile is one file of the patches folder at a commit.
type branchFile struct {
	url  string
	blob string // git blob SHA-1, checked after the download
	size int64
	data []byte // already downloaded (when the file list came from the raw files)
}

// gitBlob is the git blob SHA-1 of b (what GitHub lists for a file), so files can be compared without downloading them.
func gitBlob(b []byte) string {
	h := sha1.New()
	fmt.Fprintf(h, "blob %d\x00", len(b))
	h.Write(b)
	return hex.EncodeToString(h.Sum(nil))
}

// findBranch looks up the patches folder on the main branch: the last commit that changed it, its file list and its
// patches.meta.json. The patch files are downloaded by Ensure; a commit downloaded before is used from cacheDir.
// It returns nil, nil when that commit isn't newer than after (zero: any), without listing the files.
func findBranch(ctx context.Context, cacheDir string, after time.Time) (*Source, error) {
	sha, published, err := lastPatchesCommit(ctx)
	if err != nil {
		return nil, err
	}
	if !published.After(after) {
		return nil, nil
	}
	tag := branchTag + sha[:12]

	if cacheDir != "" {
		if s, err := loadCached(filepath.Join(cacheDir, tag)); err == nil {
			s.Origin, s.Published = "branch", published
			return s, nil
		}
	}

	files, err := listFromAPI(ctx, sha)
	if err != nil {
		files, err = listFromRaw(ctx, sha)
	}
	if err != nil {
		return nil, err
	}
	mf, ok := files[MetaFile]
	if !ok {
		return nil, fmt.Errorf("github.com/%s has no %s/%s", Repo, PatchesPath, MetaFile)
	}
	mdata := mf.data
	if mdata == nil {
		if mdata, err = download.Bytes(ctx, mf.url, nil); err != nil {
			return nil, err
		}
	}
	if gitBlob(mdata) != mf.blob {
		return nil, fmt.Errorf("%s from GitHub doesn't match its checksum", MetaFile)
	}
	m, err := manifest.Parse(mdata)
	if err != nil {
		return nil, err
	}
	m.Release = tag
	s := &Source{Manifest: m, Tag: tag, Origin: "branch", Published: published, files: map[string]branchFile{MetaFile: mf}}
	for _, p := range m.Patches {
		f, ok := files[p.File]
		if !ok {
			return nil, fmt.Errorf("github.com/%s: patch %s (%s) is missing", Repo, p.ID, p.File)
		}
		s.files[p.File] = f
		if f.data == nil {
			s.patchesSize += f.size
		}
	}
	return s, nil
}

// lastPatchesCommit returns the last commit on the main branch that changed the patches folder, from the commit
// feed, else from the API.
func lastPatchesCommit(ctx context.Context) (sha string, date time.Time, err error) {
	if data, ferr := download.Bytes(ctx, feedURL, nil); ferr == nil {
		var feed struct {
			Entries []struct {
				ID      string    `xml:"id"` // tag:github.com,2008:Grit::Commit/<sha>
				Updated time.Time `xml:"updated"`
			} `xml:"entry"`
		}
		if xml.Unmarshal(data, &feed) == nil && len(feed.Entries) > 0 {
			e := feed.Entries[0]
			if i := strings.LastIndex(e.ID, "/"); i >= 0 && len(e.ID)-i-1 == 40 && !e.Updated.IsZero() {
				return e.ID[i+1:], e.Updated, nil
			}
		}
	}
	data, err := download.Bytes(ctx, apiURL+"/commits?sha="+Branch+"&path="+PatchesPath+"&per_page=1", apiHeaders)
	if err != nil {
		return "", time.Time{}, err
	}
	var commits []struct {
		SHA    string `json:"sha"`
		Commit struct {
			Committer struct {
				Date time.Time `json:"date"`
			} `json:"committer"`
		} `json:"commit"`
	}
	if err := json.Unmarshal(data, &commits); err != nil {
		return "", time.Time{}, fmt.Errorf("unexpected answer from GitHub: %w", err)
	}
	if len(commits) == 0 || len(commits[0].SHA) != 40 {
		return "", time.Time{}, errors.New("no patches on the " + Branch + " branch of github.com/" + Repo)
	}
	return commits[0].SHA, commits[0].Commit.Committer.Date, nil
}

// listFromAPI lists the patches folder at commit sha with each file's git blob hash, so nothing is downloaded yet.
func listFromAPI(ctx context.Context, sha string) (map[string]branchFile, error) {
	data, err := download.Bytes(ctx, apiURL+"/contents/"+PatchesPath+"?ref="+sha, apiHeaders)
	if err != nil {
		return nil, err
	}
	var list []struct {
		Name string `json:"name"`
		SHA  string `json:"sha"`
		Size int64  `json:"size"`
		Type string `json:"type"`
	}
	if err := json.Unmarshal(data, &list); err != nil {
		return nil, fmt.Errorf("unexpected answer from GitHub: %w", err)
	}
	files := map[string]branchFile{}
	for _, f := range list {
		if f.Type == "file" {
			files[f.Name] = branchFile{url: rawURL + "/" + sha + "/" + PatchesPath + "/" + f.Name, blob: f.SHA, size: f.Size}
		}
	}
	return files, nil
}

// listFromRaw is the fallback when the API refuses (its hourly limit): it downloads patches.meta.json and every patch
// it lists from the raw files, which have no such limit, and hashes them itself.
func listFromRaw(ctx context.Context, sha string) (map[string]branchFile, error) {
	base := rawURL + "/" + sha + "/" + PatchesPath + "/"
	get := func(name string) (branchFile, error) {
		data, err := download.Bytes(ctx, base+name, nil)
		if err != nil {
			return branchFile{}, err
		}
		return branchFile{url: base + name, blob: gitBlob(data), size: int64(len(data)), data: data}, nil
	}
	mf, err := get(MetaFile)
	if err != nil {
		return nil, err
	}
	m, err := manifest.Parse(mf.data)
	if err != nil {
		return nil, err
	}
	files := map[string]branchFile{MetaFile: mf}
	for _, p := range m.Patches {
		if files[p.File], err = get(p.File); err != nil {
			return nil, err
		}
	}
	return files, nil
}

// sameAsBuiltin reports whether the branch patches br are the built-in ones b (same patch list and file contents).
func sameAsBuiltin(br, b *Source) bool {
	if br.files == nil { // from the cache: it has SHA-256 hashes like the built-in manifest
		return samePatches(br.Manifest, b.Manifest)
	}
	if len(br.Manifest.Patches) != len(b.Manifest.Patches) {
		return false
	}
	for name, f := range br.files {
		data, err := fs.ReadFile(b.builtin, name)
		if err != nil || gitBlob(data) != f.blob {
			return false
		}
	}
	return true
}

// ensureBranch downloads the branch patches into cacheDir/<tag> and checks them against GitHub's file list.
func (s *Source) ensureBranch(ctx context.Context, cacheDir string, progress download.Progress) error {
	if cacheDir == "" {
		return errors.New("no data folder for the patches")
	}
	dir := filepath.Join(cacheDir, s.Tag)
	if err := os.RemoveAll(dir); err != nil {
		return err
	}
	patchDir := filepath.Join(dir, "patches")
	var done int64
	for i := range s.Manifest.Patches {
		p := &s.Manifest.Patches[i]
		f := s.files[p.File]
		dst := filepath.Join(patchDir, filepath.Base(p.File))
		if f.data != nil {
			if err := os.MkdirAll(patchDir, 0o755); err != nil {
				return err
			}
			if err := os.WriteFile(dst, f.data, 0o644); err != nil {
				return err
			}
		} else {
			base := done
			err := download.File(ctx, f.url, dst, download.Hash{}, func(d, _ int64) {
				if progress != nil {
					progress(base+d, s.patchesSize)
				}
			})
			if err != nil {
				return err
			}
			done += f.size
		}
		data, err := os.ReadFile(dst)
		if err != nil {
			return err
		}
		if gitBlob(data) != f.blob {
			return fmt.Errorf("patch %s from GitHub doesn't match its checksum", p.ID)
		}
		sum, err := download.SHA256File(dst)
		if err != nil {
			return err
		}
		p.SHA256 = sum
	}
	s.Dir, s.files = patchDir, nil
	data, err := s.Manifest.Marshal()
	if err != nil {
		return err
	}
	if err := os.WriteFile(filepath.Join(dir, ManifestAsset), data, 0o644); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, ".complete"), nil, 0o644)
}

// isBranchTag: the tag of patches taken from the main branch ("main-<commit>").
func isBranchTag(tag string) bool { return strings.HasPrefix(tag, branchTag) }
