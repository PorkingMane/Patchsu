package release

import (
	"context"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"testing/fstest"
	"time"
)

func TestBuiltinPatches(t *testing.T) {
	fsys := fstest.MapFS{
		MetaFile: {Data: []byte(`{"osuCommit":"abc","patches":[{"id":"0001","file":"0001-a.patch","required":true},` +
			`{"id":"0002","file":"0002-b.patch","needs":["0001"]}],"presets":[{"name":"All","patches":"*"}]}`)},
		"0001-a.patch": {Data: []byte("one\n")},
		"0002-b.patch": {Data: []byte("two\n")},
	}
	// Open prefers the built-in patches unless a release or a folder is asked for.
	src, err := Open(context.Background(), "", "", fsys, "")
	if err != nil {
		t.Fatal(err)
	}
	if src.Origin != "builtin" || src.NeedsDownload() || src.Describe() != "built-in patches" {
		t.Fatalf("origin %s, needs download %v, %q", src.Origin, src.NeedsDownload(), src.Describe())
	}
	cache := t.TempDir()
	if err := src.Ensure(context.Background(), cache, nil); err != nil {
		t.Fatal(err)
	}
	got, err := os.ReadFile(src.PatchPath("0002"))
	if err != nil || string(got) != "two\n" {
		t.Fatalf("unpacked 0002 = %q, %v", got, err)
	}
	if filepath.Dir(src.Dir) != filepath.Join(cache, src.Tag) {
		t.Fatalf("unpacked into %s", src.Dir)
	}
	// A second build reuses the unpacked files; a damaged one is unpacked again.
	again, _ := FromFS(fsys)
	if again.Tag != src.Tag {
		t.Fatalf("tag changed: %s → %s", src.Tag, again.Tag)
	}
	if err := os.WriteFile(src.PatchPath("0001"), []byte("changed"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := again.Ensure(context.Background(), cache, nil); err != nil {
		t.Fatal(err)
	}
	if got, _ := os.ReadFile(again.PatchPath("0001")); string(got) != "one\n" {
		t.Fatalf("damaged patch not replaced: %q", got)
	}
	// Different patches get a different tag (and folder).
	fsys["0002-b.patch"] = &fstest.MapFile{Data: []byte("three\n")}
	other, _ := FromFS(fsys)
	if other.Tag == src.Tag {
		t.Fatal("tag doesn't follow the patch contents")
	}
}

// TestUpdate: a GitHub patch release replaces the built-in patches only when it came out after they were bundled
// and its patches differ; offline, the built-in ones stay.
func TestUpdate(t *testing.T) {
	meta := `{"osuCommit":"abc","patches":[{"id":"0001","file":"0001-a.patch","required":true}],"presets":[{"name":"All","patches":"*"}]}`
	builtin := fstest.MapFS{
		MetaFile:       {Data: []byte(meta)},
		"0001-a.patch": {Data: []byte("one\n")},
		BundledAtFile:  {Data: []byte("2026-11-20T10:00:00Z\n")},
	}
	b, err := FromFS(builtin)
	if err != nil {
		t.Fatal(err)
	}
	if !b.Bundled.Equal(time.Date(2026, 11, 20, 10, 0, 0, 0, time.UTC)) {
		t.Fatalf("bundled-at not read: %v", b.Bundled)
	}

	// A fake GitHub with one patch release; its manifest's 0001 hash is set per case.
	var published, hash string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/releases":
			fmt.Fprintf(w, `[{"tag_name":"cli-v0.2.0","published_at":"2026-11-30T00:00:00Z","assets":[]},
 {"tag_name":"v27","published_at":%q,"assets":[{"name":"manifest.json","browser_download_url":"%[2]s/manifest.json","size":1},
  {"name":"patches.zip","browser_download_url":"%[2]s/patches.zip","size":1}]}]`, published, "http://"+r.Host)
		case "/manifest.json":
			fmt.Fprintf(w, `{"release":"v27","osuCommit":"abc","patches":[{"id":"0001","file":"0001-a.patch","required":true,"sha256":%q}],
 "presets":[{"name":"All","patches":"*"}]}`, hash)
		default:
			http.NotFound(w, r)
		}
	}))
	defer srv.Close()
	old, oldAPI, oldFeed := releasesURL, apiURL, feedURL
	// The branch can't be read: releases are used.
	releasesURL, apiURL, feedURL = srv.URL+"/releases", srv.URL+"/no-branch", srv.URL+"/no-feed"
	defer func() { releasesURL, apiURL, feedURL = old, oldAPI, oldFeed }()

	ctx := context.Background()
	same := b.Manifest.Patches[0].SHA256
	for _, c := range []struct {
		name, published, hash string
		update                bool
	}{
		{"newer, changed", "2026-11-25T00:00:00Z", strings.Repeat("ab", 32), true},
		{"newer, same patches", "2026-11-25T00:00:00Z", same, false},
		{"older, changed", "2026-11-19T00:00:00Z", strings.Repeat("ab", 32), false},
	} {
		published, hash = c.published, c.hash
		up, notice := Update(ctx, b, "", "0.1.0")
		if (up != nil) != c.update {
			t.Errorf("%s: update %v, notice %q", c.name, up != nil, notice)
			continue
		}
		if up != nil && (up.Tag != "v27" || up.Origin != "github" || !up.NeedsDownload() || !strings.Contains(notice, "v27")) {
			t.Errorf("%s: %+v, %q", c.name, up, notice)
		}
	}

	// Offline: no update, no notice.
	releasesURL, apiURL, feedURL = "http://127.0.0.1:1/releases", "http://127.0.0.1:1", "http://127.0.0.1:1"
	if up, notice := Update(ctx, b, "", "0.1.0"); up != nil || notice != "" {
		t.Errorf("offline: %v %q", up, notice)
	}
}

// TestUpdateBranch: patches changed on the main branch after the CLI was built replace the built-in ones, are
// downloaded and checked by Ensure, and are used from the cache next time.
func TestUpdateBranch(t *testing.T) {
	meta := `{"osuCommit":"abc","patches":[{"id":"0001","file":"0001-a.patch","required":true}],"presets":[{"name":"All","patches":"*"}]}`
	builtin := fstest.MapFS{
		MetaFile:       {Data: []byte(meta)},
		"0001-a.patch": {Data: []byte("one\n")},
		BundledAtFile:  {Data: []byte("2026-11-20T10:00:00Z\n")},
	}
	b, err := FromFS(builtin)
	if err != nil {
		t.Fatal(err)
	}
	var date, patch string
	commit := strings.Repeat("c", 40)
	feed, apiLimited := true, false
	listed := 0 // file listings asked for
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if apiLimited && strings.HasPrefix(r.URL.Path, "/api/") {
			w.WriteHeader(http.StatusForbidden)
			return
		}
		switch r.URL.Path {
		case "/feed":
			if !feed {
				http.NotFound(w, r)
				return
			}
			fmt.Fprintf(w, `<?xml version="1.0" encoding="UTF-8"?><feed xmlns="http://www.w3.org/2005/Atom"><id>x</id><updated>%[2]s</updated>
<entry><id>tag:github.com,2008:Grit::Commit/%[1]s</id><updated>%[2]s</updated></entry>
<entry><id>tag:github.com,2008:Grit::Commit/%[3]s</id><updated>2020-01-01T00:00:00Z</updated></entry></feed>`, commit, date, strings.Repeat("0", 40))
		case "/api/commits":
			fmt.Fprintf(w, `[{"sha":%q,"commit":{"committer":{"date":%q}}}]`, commit, date)
		case "/api/contents/" + PatchesPath:
			listed++
			fmt.Fprintf(w, `[{"name":%q,"sha":%q,"size":%d,"type":"file"},{"name":"0001-a.patch","sha":%q,"size":%d,"type":"file"}]`,
				MetaFile, gitBlob([]byte(meta)), len(meta), gitBlob([]byte(patch)), len(patch))
		case "/raw/" + commit + "/" + PatchesPath + "/" + MetaFile:
			fmt.Fprint(w, meta)
		case "/raw/" + commit + "/" + PatchesPath + "/0001-a.patch":
			fmt.Fprint(w, patch)
		default:
			http.NotFound(w, r)
		}
	}))
	defer srv.Close()
	oldAPI, oldRaw, oldRel := apiURL, rawURL, releasesURL
	oldFeed := feedURL
	apiURL, rawURL, releasesURL, feedURL = srv.URL+"/api", srv.URL+"/raw", srv.URL+"/releases", srv.URL+"/feed"
	defer func() { apiURL, rawURL, releasesURL, feedURL = oldAPI, oldRaw, oldRel, oldFeed }()

	ctx := context.Background()
	for _, c := range []struct {
		name, date, patch string
		update            bool
	}{
		{"newer, same patches", "2026-11-25T00:00:00Z", "one\n", false},
		{"older, changed", "2026-11-19T00:00:00Z", "two\n", false},
	} {
		date, patch = c.date, c.patch
		if up, notice := Update(ctx, b, t.TempDir(), "0.1.0"); (up != nil) != c.update {
			t.Errorf("%s: update %v, notice %q", c.name, up != nil, notice)
		}
	}
	if listed != 1 {
		t.Errorf("the files were listed %d times; an older commit shouldn't be listed at all", listed)
	}

	date, patch = "2026-11-25T00:00:00Z", "two\n"
	cache := t.TempDir()
	up, notice := Update(ctx, b, cache, "0.1.0")
	if up == nil || up.Origin != "branch" || !up.NeedsDownload() || !strings.Contains(notice, "Updated") ||
		up.Describe() != "GitHub patches (main@ccccccc)" {
		t.Fatalf("newer, changed: %+v, %q", up, notice)
	}
	if err := up.Ensure(ctx, cache, nil); err != nil {
		t.Fatal(err)
	}
	if got, _ := os.ReadFile(up.PatchPath("0001")); string(got) != "two\n" {
		t.Fatalf("downloaded patch: %q", got)
	}
	// Next start: from the cache, no download.
	again, _ := Update(ctx, b, cache, "0.1.0")
	if again == nil || again.Origin != "branch" || again.NeedsDownload() {
		t.Fatalf("cached: %+v", again)
	}

	// A file that doesn't match GitHub's checksum is refused.
	patch = "three\n"
	commit = strings.Repeat("d", 40)
	up, _ = Update(ctx, b, cache, "0.1.0")
	if up == nil {
		t.Fatal("no update for the second commit")
	}
	patch = "tampered\n"
	if err := up.Ensure(ctx, cache, nil); err == nil {
		t.Fatal("a patch with the wrong checksum was accepted")
	}

	// No feed and the API at its hourly limit: the raw files are downloaded and compared instead.
	feed, apiLimited = false, true
	if up, _ := Update(ctx, b, t.TempDir(), "0.1.0"); up != nil {
		t.Fatal("API limited, no feed: GitHub can't be read, but an update was found")
	}
	feed, patch = true, "four\n"
	commit = strings.Repeat("e", 40)
	up, _ = Update(ctx, b, "", "0.1.0")
	if up == nil || up.NeedsDownload() && up.DownloadSize() != 0 {
		t.Fatalf("API limited: %+v", up)
	}
	if err := up.Ensure(ctx, cache, nil); err != nil {
		t.Fatal(err)
	}
	if got, _ := os.ReadFile(up.PatchPath("0001")); string(got) != "four\n" {
		t.Fatalf("raw patch: %q", got)
	}
	patch = "one\n"
	commit = strings.Repeat("f", 40)
	if up, _ := Update(ctx, b, t.TempDir(), "0.1.0"); up != nil {
		t.Fatal("API limited: same patches as built in, but an update was found")
	}

	// Offline: the cached branch patches are found for --release main.
	apiURL, feedURL = "http://127.0.0.1:1", "http://127.0.0.1:1"
	s, err := Open(ctx, "", Branch, nil, cache)
	if err != nil || s.Origin != "cache" || !strings.Contains(s.Describe(), "offline") {
		t.Fatalf("offline --release main: %+v %v", s, err)
	}
}
