package deps

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/download"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

type dotnetFile struct {
	Name string `json:"name"`
	RID  string `json:"rid"`
	URL  string `json:"url"`
	Hash string `json:"hash"` // SHA-512
}

type dotnetReleases struct {
	LatestSDK string `json:"latest-sdk"`
	Releases  []struct {
		SDK struct {
			Version string       `json:"version"`
			Files   []dotnetFile `json:"files"`
		} `json:"sdk"`
	} `json:"releases"`
}

// installDotnet downloads the newest SDK of the channel from Microsoft's official release metadata, checks its SHA-512
// and unpacks it into data/tools/dotnet. Nothing is installed system-wide.
func installDotnet(ctx context.Context, info *sysinfo.Info, channel string, data paths.Data, p Progress) error {
	p("Looking up the newest .NET "+channel+" SDK", -1, -1)
	meta, err := download.Bytes(ctx, "https://builds.dotnet.microsoft.com/dotnet/release-metadata/"+channel+"/releases.json", nil)
	if err != nil {
		return fmt.Errorf("reading .NET release list: %w", err)
	}
	var rel dotnetReleases
	if err := json.Unmarshal(meta, &rel); err != nil {
		return fmt.Errorf("reading .NET release list: %w", err)
	}
	ext := ".tar.gz"
	if info.OS == "windows" {
		ext = ".zip"
	}
	var file *dotnetFile
	version := ""
	for _, r := range rel.Releases {
		if r.SDK.Version != rel.LatestSDK {
			continue
		}
		for i, f := range r.SDK.Files {
			if f.RID == info.RID && strings.HasSuffix(f.Name, ext) {
				file, version = &r.SDK.Files[i], r.SDK.Version
				break
			}
		}
		break
	}
	if file == nil {
		return fmt.Errorf("no .NET SDK %s download for %s", channel, info.RID)
	}

	archive := filepath.Join(data.Downloads(), filepath.Base(file.URL))
	err = download.File(ctx, file.URL, archive, download.Hash{Algo: "sha512", Hex: file.Hash}, func(d, t int64) {
		p("Downloading .NET SDK "+version, d, t)
	})
	if err != nil {
		return err
	}
	defer os.Remove(archive)

	p("Unpacking .NET SDK "+version, -1, -1)
	dir := data.Dotnet()
	if err := os.RemoveAll(dir); err != nil {
		return err
	}
	if ext == ".zip" {
		err = download.Unzip(archive, dir, false)
	} else {
		err = download.Untargz(archive, dir)
	}
	if err != nil {
		os.RemoveAll(dir)
		return fmt.Errorf("unpacking the .NET SDK: %w", err)
	}
	return nil
}

type ghAsset struct {
	Name   string `json:"name"`
	URL    string `json:"browser_download_url"`
	Digest string `json:"digest"` // "sha256:..." on newer releases
}

// installMinGit downloads the portable MinGit (Windows) into data/tools/git. No admin rights needed.
func installMinGit(ctx context.Context, info *sysinfo.Info, data paths.Data, p Progress) error {
	p("Looking up the newest MinGit", -1, -1)
	body, err := download.Bytes(ctx, "https://api.github.com/repos/git-for-windows/git/releases/latest",
		map[string]string{"Accept": "application/vnd.github+json"})
	if err != nil {
		return fmt.Errorf("reading the git-for-windows release: %w", err)
	}
	var rel struct {
		Assets []ghAsset `json:"assets"`
	}
	if err := json.Unmarshal(body, &rel); err != nil {
		return err
	}
	suffix := "-64-bit.zip"
	if info.Arch == "arm64" {
		suffix = "-arm64.zip"
	}
	var asset *ghAsset
	for i, a := range rel.Assets {
		if strings.HasPrefix(a.Name, "MinGit-") && strings.HasSuffix(a.Name, suffix) && !strings.Contains(a.Name, "busybox") {
			asset = &rel.Assets[i]
			break
		}
	}
	if asset == nil {
		return fmt.Errorf("no MinGit download found for %s", info.Arch)
	}
	want := download.Hash{Algo: "sha256", Hex: strings.TrimPrefix(asset.Digest, "sha256:")}
	archive := filepath.Join(data.Downloads(), asset.Name)
	if err := download.File(ctx, asset.URL, archive, want, func(d, t int64) { p("Downloading "+asset.Name, d, t) }); err != nil {
		return err
	}
	defer os.Remove(archive)
	p("Unpacking MinGit", -1, -1)
	dir := data.Git()
	if err := os.RemoveAll(dir); err != nil {
		return err
	}
	if err := download.Unzip(archive, dir, false); err != nil {
		os.RemoveAll(dir)
		return fmt.Errorf("unpacking MinGit: %w", err)
	}
	return nil
}
