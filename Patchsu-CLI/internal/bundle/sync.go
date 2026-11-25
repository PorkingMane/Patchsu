//go:build ignore

// sync.go copies the logo and the patches into this package, so they're built into the CLI.
// Run it with `go generate ./...` from Patchsu-CLI (install-dev.sh does) before building a release binary.
package main

import (
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

func main() {
	repo := filepath.Join("..", "..", "..")
	must(copyFile(filepath.Join(repo, "assets", "logo.png"), filepath.Join("logo", "patchsu.png")))
	must(copyFile(filepath.Join(repo, "assets", "patchsu.ico"), filepath.Join("logo", "patchsu.ico")))

	from := filepath.Join(repo, "Patchsu", "patches")
	if _, err := os.Stat(filepath.Join(from, "patches.meta.json")); err != nil {
		fmt.Fprintln(os.Stderr, "bundle: no patches.meta.json in", from, "- the CLI is built without patches")
		return
	}
	// Start clean, so patches removed from the repo don't stay in the binary. README.md stays.
	old, _ := filepath.Glob(filepath.Join("patches", "*"))
	for _, p := range old {
		if filepath.Base(p) != "README.md" {
			must(os.RemoveAll(p))
		}
	}
	entries, err := os.ReadDir(from)
	must(err)
	n := 0
	for _, e := range entries {
		name := e.Name()
		if e.IsDir() || !(strings.HasSuffix(name, ".patch") || name == "patches.meta.json") {
			continue
		}
		must(copyFile(filepath.Join(from, name), filepath.Join("patches", name)))
		if strings.HasSuffix(name, ".patch") {
			n++
		}
	}
	at := bundledAt(from)
	must(os.WriteFile(filepath.Join("patches", "bundled-at"), []byte(at.Format(time.RFC3339)+"\n"), 0o644))
	fmt.Printf("bundle: logo + %d patches from %s (changed %s)\n", n, from, at.Format(time.RFC3339))
}

// bundledAt is when the patches last changed: the last commit that touched them, or now when they have uncommitted
// changes (or git isn't there). The CLI uses a GitHub patch release instead only if it was published later.
func bundledAt(dir string) time.Time {
	if out, err := exec.Command("git", "-C", dir, "status", "--porcelain", "--", ".").Output(); err != nil || len(out) > 0 {
		return time.Now().UTC().Truncate(time.Second)
	}
	out, err := exec.Command("git", "-C", dir, "log", "-1", "--format=%cI", "--", ".").Output()
	if err != nil {
		return time.Now().UTC().Truncate(time.Second)
	}
	t, err := time.Parse(time.RFC3339, strings.TrimSpace(string(out)))
	if err != nil {
		return time.Now().UTC().Truncate(time.Second)
	}
	return t.UTC()
}

func copyFile(from, to string) error {
	in, err := os.Open(from)
	if err != nil {
		return err
	}
	defer in.Close()
	if err := os.MkdirAll(filepath.Dir(to), 0o755); err != nil {
		return err
	}
	out, err := os.Create(to)
	if err != nil {
		return err
	}
	if _, err := io.Copy(out, in); err != nil {
		out.Close()
		return err
	}
	return out.Close()
}

func must(err error) {
	if err != nil {
		fmt.Fprintln(os.Stderr, "bundle:", err)
		os.Exit(1)
	}
}
