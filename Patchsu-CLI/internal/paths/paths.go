// Package paths knows the layout of the data folder and the install locations.
package paths

import (
	"os"
	"path/filepath"
	"strings"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// DataDirName is the folder created next to the program or in Documents.
const DataDirName = "patchsu-data"

// Data is the layout of the data folder.
type Data struct{ Root string }

func (d Data) Tools() string     { return filepath.Join(d.Root, "tools") }
func (d Data) Dotnet() string    { return filepath.Join(d.Root, "tools", "dotnet") }
func (d Data) Git() string       { return filepath.Join(d.Root, "tools", "git") }
func (d Data) Releases() string  { return filepath.Join(d.Root, "releases") }
func (d Data) Source() string    { return filepath.Join(d.Root, "osu-lazer") }
func (d Data) NuGet() string     { return filepath.Join(d.Root, "nuget") }
func (d Data) Logs() string      { return filepath.Join(d.Root, "logs") }
func (d Data) Downloads() string { return filepath.Join(d.Root, "downloads") }

// Install describes where the game is installed and its launcher.
type Install struct {
	// Name is the game's name: "Patchsu", or "Patchsu-<Server>" for a custom server build.
	Name string
	// Prefix is the folder dotnet publishes into (on macOS: Patchsu.app/Contents/MacOS).
	Prefix string
	// AppBundle is the .app folder on macOS, "" elsewhere.
	AppBundle string
	// Launcher is the .desktop file (Linux) or .lnk shortcut (Windows), "" on macOS.
	Launcher string
	// Executable is the game binary after renaming.
	Executable string
}

// BuildInfoName is written into the install folder after a build.
const BuildInfoName = "patchsu-build.json"

// BuildInfo returns the path of patchsu-build.json.
func (i Install) BuildInfo() string { return filepath.Join(i.Prefix, BuildInfoName) }

// Slug is the lower-case form of a build name used for folders and the menu entry: "patchsu", "patchsu-gatari".
func Slug(name string) string {
	if name == "" {
		name = "Patchsu"
	}
	return strings.ToLower(name)
}

// DefaultInstall returns the standard install location of the build called name ("Patchsu" or "Patchsu-<Server>";
// "" means "Patchsu") on this system. prefix overrides the folder (on macOS: the .app).
func DefaultInstall(info *sysinfo.Info, name, prefix string) Install {
	if name == "" {
		name = "Patchsu"
	}
	if prefix == "" {
		prefix = defaultFolder(info, name)
	}
	return installAt(info, name, prefix)
}

// InstallIn returns the install of the build called name inside the folder parent: <parent>/<name>
// (<parent>/<name>.app on macOS). An empty parent gives the standard location.
func InstallIn(info *sysinfo.Info, name, parent string) Install {
	if name == "" {
		name = "Patchsu"
	}
	if parent == "" {
		return DefaultInstall(info, name, "")
	}
	return installAt(info, name, filepath.Join(parent, GameFolder(info, name)))
}

// Locate is the install the CLI uses: --prefix if given, else the folder chosen earlier (parent), else the standard one.
func Locate(info *sysinfo.Info, name, prefix, parent string) Install {
	if prefix != "" {
		return DefaultInstall(info, name, prefix)
	}
	return InstallIn(info, name, parent)
}

// GameFolder is the name of the game's folder inside a chosen parent folder: "Patchsu", or "Patchsu.app" on macOS.
func GameFolder(info *sysinfo.Info, name string) string {
	if info.OS == "darwin" {
		return name + ".app"
	}
	return name
}

// Root is the folder the install owns: the .app on macOS, else Prefix.
func (i Install) Root() string {
	if i.AppBundle != "" {
		return i.AppBundle
	}
	return i.Prefix
}

// Occupied reports whether the install's folder holds something that isn't a Patchsu build (no patchsu-build.json
// and no game executable, which builds from build-patchsu.sh have): building there would mix files into it, and
// uninstalling would delete it.
func Occupied(i Install) bool {
	entries, err := os.ReadDir(i.Root())
	if err != nil || len(entries) == 0 {
		return false
	}
	for _, p := range []string{i.BuildInfo(), i.Executable} {
		if _, err := os.Stat(p); err == nil {
			return false
		}
	}
	return true
}

func defaultFolder(info *sysinfo.Info, name string) string {
	slug := Slug(name)
	switch info.OS {
	case "darwin":
		return filepath.Join(info.HomeDir, "Applications", name+".app")
	case "windows":
		return filepath.Join(os.Getenv("LOCALAPPDATA"), slug+"-app")
	default:
		return filepath.Join(dataHome(info), slug+"-app")
	}
}

func dataHome(info *sysinfo.Info) string {
	if d := os.Getenv("XDG_DATA_HOME"); d != "" {
		return d
	}
	return filepath.Join(info.HomeDir, ".local", "share")
}

// installAt lays out an install whose own folder is dir (the .app on macOS).
func installAt(info *sysinfo.Info, name, dir string) Install {
	switch info.OS {
	case "darwin":
		mac := filepath.Join(dir, "Contents", "MacOS")
		return Install{Name: name, Prefix: mac, AppBundle: dir, Executable: filepath.Join(mac, name)}
	case "windows":
		lnk := filepath.Join(os.Getenv("APPDATA"), "Microsoft", "Windows", "Start Menu", "Programs", name+".lnk")
		return Install{Name: name, Prefix: dir, Launcher: lnk, Executable: filepath.Join(dir, name+".exe")}
	default:
		return Install{
			Name:       name,
			Prefix:     dir,
			Launcher:   filepath.Join(dataHome(info), "applications", Slug(name)+".desktop"),
			Executable: filepath.Join(dir, name),
		}
	}
}
