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
	old := releasesURL
	releasesURL = srv.URL + "/releases"
	defer func() { releasesURL = old }()

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
	releasesURL = "http://127.0.0.1:1/releases"
	if up, notice := Update(ctx, b, "", "0.1.0"); up != nil || notice != "" {
		t.Errorf("offline: %v %q", up, notice)
	}
}
