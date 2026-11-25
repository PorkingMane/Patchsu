// Package download fetches files over HTTP with progress, checks hashes and unpacks archives.
package download

import (
	"archive/tar"
	"archive/zip"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"crypto/sha512"
	"encoding/hex"
	"fmt"
	"hash"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// UserAgent is sent with every request (GitHub's API requires one).
const UserAgent = "Patchsu-CLI"

// Progress is called while downloading. total is -1 when the server doesn't say.
type Progress func(done, total int64)

var client = &http.Client{Timeout: 0}

// Request makes a GET request with the CLI's headers.
func Request(ctx context.Context, url string, headers map[string]string) (*http.Response, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("User-Agent", UserAgent)
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	if resp.StatusCode != http.StatusOK {
		resp.Body.Close()
		return nil, &StatusError{URL: url, Code: resp.StatusCode}
	}
	return resp, nil
}

// StatusError is a non-200 HTTP response.
type StatusError struct {
	URL  string
	Code int
}

func (e *StatusError) Error() string { return fmt.Sprintf("%s: HTTP %d", e.URL, e.Code) }

// Bytes downloads a small file into memory (max 16 MB).
func Bytes(ctx context.Context, url string, headers map[string]string) ([]byte, error) {
	ctx, cancel := context.WithTimeout(ctx, 30*time.Second)
	defer cancel()
	resp, err := Request(ctx, url, headers)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	return io.ReadAll(io.LimitReader(resp.Body, 16<<20))
}

// Hash says which hash to check after a download. An empty Hex means no check.
type Hash struct {
	Algo string // "sha256" or "sha512"
	Hex  string
}

func (h Hash) new() hash.Hash {
	if h.Algo == "sha512" {
		return sha512.New()
	}
	return sha256.New()
}

// File downloads url to dst. It writes to dst+".part" first and renames only after the download (and hash, if given)
// succeeded, so a half-finished file is never used.
func File(ctx context.Context, url, dst string, want Hash, progress Progress) error {
	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}
	resp, err := Request(ctx, url, nil)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	part := dst + ".part"
	f, err := os.Create(part)
	if err != nil {
		return err
	}
	h := want.new()
	pw := &progressWriter{total: resp.ContentLength, fn: progress}
	_, err = io.Copy(io.MultiWriter(f, h, pw), resp.Body)
	if cerr := f.Close(); err == nil {
		err = cerr
	}
	if err != nil {
		os.Remove(part)
		return fmt.Errorf("downloading %s: %w", url, err)
	}
	if want.Hex != "" {
		if got := hex.EncodeToString(h.Sum(nil)); !strings.EqualFold(got, want.Hex) {
			os.Remove(part)
			return fmt.Errorf("downloaded file %s has the wrong %s checksum (got %s, expected %s)", filepath.Base(dst), want.Algo, got, want.Hex)
		}
	}
	return os.Rename(part, dst)
}

type progressWriter struct {
	done, total int64
	fn          Progress
	last        time.Time
}

func (p *progressWriter) Write(b []byte) (int, error) {
	p.done += int64(len(b))
	if p.fn != nil && (time.Since(p.last) > 100*time.Millisecond || p.done == p.total) {
		p.last = time.Now()
		p.fn(p.done, p.total)
	}
	return len(b), nil
}

// SHA256File returns the hex SHA-256 of a file.
func SHA256File(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err := io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

// safeJoin joins name under dir and refuses paths that would escape it.
func safeJoin(dir, name string) (string, error) {
	p := filepath.Join(dir, name)
	if p != filepath.Clean(dir) && !strings.HasPrefix(p, filepath.Clean(dir)+string(os.PathSeparator)) {
		return "", fmt.Errorf("archive entry %q points outside the target folder", name)
	}
	return p, nil
}

// Unzip extracts a zip archive into dir. With flatten, only regular files are extracted, all directly into dir.
func Unzip(archive, dir string, flatten bool) error {
	r, err := zip.OpenReader(archive)
	if err != nil {
		return err
	}
	defer r.Close()
	for _, f := range r.File {
		name := f.Name
		if flatten {
			if f.FileInfo().IsDir() {
				continue
			}
			name = filepath.Base(filepath.FromSlash(name))
		}
		dst, err := safeJoin(dir, filepath.FromSlash(name))
		if err != nil {
			return err
		}
		if f.FileInfo().IsDir() {
			if err := os.MkdirAll(dst, 0o755); err != nil {
				return err
			}
			continue
		}
		if err := writeEntry(dst, f.Mode(), func() (io.ReadCloser, error) { return f.Open() }); err != nil {
			return err
		}
	}
	return nil
}

func writeEntry(dst string, mode os.FileMode, open func() (io.ReadCloser, error)) error {
	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}
	rc, err := open()
	if err != nil {
		return err
	}
	defer rc.Close()
	perm := mode.Perm()
	if perm == 0 {
		perm = 0o644
	}
	out, err := os.OpenFile(dst, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, perm|0o200)
	if err != nil {
		return err
	}
	_, err = io.Copy(out, rc)
	if cerr := out.Close(); err == nil {
		err = cerr
	}
	return err
}

// Untargz extracts a .tar.gz archive into dir (regular files, directories and symlinks inside dir).
func Untargz(archive, dir string) error {
	f, err := os.Open(archive)
	if err != nil {
		return err
	}
	defer f.Close()
	gz, err := gzip.NewReader(f)
	if err != nil {
		return err
	}
	defer gz.Close()
	tr := tar.NewReader(gz)
	for {
		hdr, err := tr.Next()
		if err == io.EOF {
			return nil
		}
		if err != nil {
			return err
		}
		dst, err := safeJoin(dir, filepath.FromSlash(hdr.Name))
		if err != nil {
			return err
		}
		switch hdr.Typeflag {
		case tar.TypeDir:
			if err := os.MkdirAll(dst, 0o755); err != nil {
				return err
			}
		case tar.TypeReg:
			if err := writeEntry(dst, os.FileMode(hdr.Mode), func() (io.ReadCloser, error) { return io.NopCloser(tr), nil }); err != nil {
				return err
			}
		case tar.TypeSymlink:
			target := filepath.Join(filepath.Dir(dst), hdr.Linkname)
			if _, err := safeJoin(dir, mustRel(dir, target)); err != nil || filepath.IsAbs(hdr.Linkname) {
				return fmt.Errorf("archive symlink %q points outside the target folder", hdr.Name)
			}
			os.Remove(dst)
			if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
				return err
			}
			if err := os.Symlink(hdr.Linkname, dst); err != nil {
				return err
			}
		}
	}
}

func mustRel(base, target string) string {
	rel, err := filepath.Rel(base, target)
	if err != nil {
		return ".." // treated as outside
	}
	return rel
}

// HumanBytes formats a byte count, e.g. "231 MB".
func HumanBytes(n int64) string {
	switch {
	case n >= 1<<30:
		return fmt.Sprintf("%.1f GB", float64(n)/(1<<30))
	case n >= 1<<20:
		return fmt.Sprintf("%d MB", n>>20)
	case n >= 1<<10:
		return fmt.Sprintf("%d KB", n>>10)
	}
	return fmt.Sprintf("%d B", n)
}
