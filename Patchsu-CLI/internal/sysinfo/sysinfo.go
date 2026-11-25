// Package sysinfo detects the operating system, architecture, Linux distribution and well-known folders.
package sysinfo

import (
	"bufio"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
)

// Info describes the machine the CLI runs on.
type Info struct {
	OS         string // "linux", "darwin", "windows"
	Arch       string // "x64", "arm64"
	RID        string // .NET runtime identifier, e.g. "linux-x64"
	Distro     string // Linux: PRETTY_NAME from /etc/os-release
	PkgMgr     *PackageManager
	IsRoot     bool
	HomeDir    string
	Executable string // folder of this program
}

// PackageManager is a system package manager the CLI knows how to install git with.
type PackageManager struct {
	Name      string   // "pacman"
	Install   []string // command without sudo, e.g. ["pacman", "-S", "--needed", "--noconfirm", "git"]
	NeedsRoot bool
}

// Detect gathers the system information.
func Detect() *Info {
	info := &Info{OS: runtime.GOOS, Arch: "x64"}
	if runtime.GOARCH == "arm64" {
		info.Arch = "arm64"
	}
	ridOS := map[string]string{"linux": "linux", "darwin": "osx", "windows": "win"}[runtime.GOOS]
	info.RID = ridOS + "-" + info.Arch
	info.HomeDir, _ = os.UserHomeDir()
	if exe, err := os.Executable(); err == nil {
		if resolved, err := filepath.EvalSymlinks(exe); err == nil {
			exe = resolved
		}
		info.Executable = filepath.Dir(exe)
	}
	info.IsRoot = isRoot()
	switch info.OS {
	case "linux":
		info.Distro, info.PkgMgr = linuxDistro()
	case "darwin":
		if _, err := exec.LookPath("brew"); err == nil {
			info.PkgMgr = &PackageManager{Name: "brew", Install: []string{"brew", "install", "git"}}
		}
	}
	return info
}

// OSName is "Linux", "macOS" or "Windows".
func (i *Info) OSName() string {
	return map[string]string{"linux": "Linux", "darwin": "macOS", "windows": "Windows"}[i.OS]
}

// Summary is one line, e.g. "Linux x64 · CachyOS (pacman)".
func (i *Info) Summary() string {
	s := i.OSName() + " " + i.Arch
	if i.Distro != "" {
		s += " · " + i.Distro
	}
	if i.PkgMgr != nil {
		s += " (" + i.PkgMgr.Name + ")"
	}
	return s
}

// ExeName adds ".exe" on Windows.
func (i *Info) ExeName(name string) string {
	if i.OS == "windows" {
		return name + ".exe"
	}
	return name
}

func linuxDistro() (string, *PackageManager) {
	vals := map[string]string{}
	if f, err := os.Open("/etc/os-release"); err == nil {
		sc := bufio.NewScanner(f)
		for sc.Scan() {
			k, v, ok := strings.Cut(sc.Text(), "=")
			if ok {
				vals[k] = strings.Trim(v, `"'`)
			}
		}
		f.Close()
	}
	ids := strings.Fields(vals["ID"] + " " + vals["ID_LIKE"])

	known := []struct {
		ids []string
		pm  PackageManager
	}{
		{[]string{"arch", "cachyos", "manjaro", "endeavouros"}, PackageManager{Name: "pacman", Install: []string{"pacman", "-S", "--needed", "--noconfirm", "git"}}},
		{[]string{"debian", "ubuntu", "linuxmint", "pop"}, PackageManager{Name: "apt", Install: []string{"apt-get", "install", "-y", "git"}}},
		{[]string{"fedora", "rhel", "centos", "rocky", "almalinux", "nobara"}, PackageManager{Name: "dnf", Install: []string{"dnf", "install", "-y", "git"}}},
		{[]string{"opensuse", "suse", "opensuse-tumbleweed", "opensuse-leap"}, PackageManager{Name: "zypper", Install: []string{"zypper", "--non-interactive", "install", "git"}}},
		{[]string{"alpine"}, PackageManager{Name: "apk", Install: []string{"apk", "add", "git"}}},
		{[]string{"void"}, PackageManager{Name: "xbps", Install: []string{"xbps-install", "-Sy", "git"}}},
		{[]string{"gentoo"}, PackageManager{Name: "emerge", Install: []string{"emerge", "--noreplace", "dev-vcs/git"}}},
		{[]string{"nixos"}, PackageManager{Name: "nix", Install: nil}}, // NixOS: declarative, no imperative install
	}
	var pm *PackageManager
	for _, id := range ids {
		for _, k := range known {
			for _, kid := range k.ids {
				if id == kid {
					p := k.pm
					pm = &p
					break
				}
			}
			if pm != nil {
				break
			}
		}
		if pm != nil {
			break
		}
	}
	// Unknown distro: use whichever known package manager binary exists.
	if pm == nil {
		for _, k := range known {
			if k.pm.Install == nil {
				continue
			}
			if _, err := exec.LookPath(k.pm.Install[0]); err == nil {
				p := k.pm
				pm = &p
				break
			}
		}
	}
	if pm != nil && pm.Install != nil {
		if _, err := exec.LookPath(pm.Install[0]); err != nil {
			pm = nil
		} else {
			pm.NeedsRoot = true
		}
	}
	if pm != nil && pm.Install == nil {
		pm = nil
	}
	name := vals["PRETTY_NAME"]
	if name == "" {
		name = vals["NAME"]
	}
	return name, pm
}

// ElevatedCommand returns the package manager's install command, prefixed with sudo/doas when needed.
// It returns an error when root is needed but neither sudo nor doas exists.
func (i *Info) ElevatedCommand(pm *PackageManager) ([]string, error) {
	if !pm.NeedsRoot || i.IsRoot {
		return pm.Install, nil
	}
	for _, elev := range []string{"sudo", "doas"} {
		if _, err := exec.LookPath(elev); err == nil {
			return append([]string{elev}, pm.Install...), nil
		}
	}
	return nil, fmt.Errorf("installing git needs root, but neither sudo nor doas was found")
}

// DocumentsDir returns the user's Documents folder.
func (i *Info) DocumentsDir() string {
	if d := documentsDir(); d != "" {
		return d
	}
	if i.OS == "linux" {
		if out, err := exec.Command("xdg-user-dir", "DOCUMENTS").Output(); err == nil {
			if d := strings.TrimSpace(string(out)); d != "" && d != i.HomeDir {
				return d
			}
		}
	}
	return filepath.Join(i.HomeDir, "Documents")
}

// ConfigDir is where Patchsu-CLI.json lives.
func ConfigDir() string {
	d, err := os.UserConfigDir()
	if err != nil {
		h, _ := os.UserHomeDir()
		d = filepath.Join(h, ".config")
	}
	return filepath.Join(d, "Patchsu-CLI")
}

// Writable reports whether dir (or the nearest existing parent) can be written to.
func Writable(dir string) bool {
	for d := dir; ; d = filepath.Dir(d) {
		if _, err := os.Stat(d); err == nil {
			f, err := os.CreateTemp(d, ".patchsu-write-test-*")
			if err != nil {
				return false
			}
			name := f.Name()
			f.Close()
			os.Remove(name)
			return true
		}
		if filepath.Dir(d) == d {
			return false
		}
	}
}

// FreeSpace returns the free bytes on the disk holding dir (or its nearest existing parent), or -1 if unknown.
func FreeSpace(dir string) int64 {
	for d := dir; ; d = filepath.Dir(d) {
		if _, err := os.Stat(d); err == nil {
			return freeSpace(d)
		}
		if filepath.Dir(d) == d {
			return -1
		}
	}
}
