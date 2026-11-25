// Package bundle holds what is built into the Patchsu-CLI binary: the Patchsu logo and the patches.
//
// The files are copies. `go generate ./...` (sync.go) refreshes them from ../assets and ../Patchsu/patches.
// The patch copies aren't committed: a CLI built without `go generate` has no built-in patches and
// takes them from the newest GitHub patch release instead.
package bundle

import (
	"embed"
	"io/fs"
)

//go:generate go run sync.go

// MetaFile is the patch list the built-in patches come with (the same file as in Patchsu/patches).
const MetaFile = "patches.meta.json"

//go:embed logo/patchsu.png
var logoPNG []byte

//go:embed logo/patchsu.ico
var logoICO []byte

//go:embed all:patches
var patchFiles embed.FS

// LogoPNG is the Patchsu logo (534 px PNG): the Linux menu icon and the source of the macOS app icon.
func LogoPNG() []byte { return logoPNG }

// LogoICO is the Patchsu logo as a Windows icon (16–256 px). It replaces osu!'s lazer.ico before the build,
// so it becomes the .exe icon and the game's window icon on every system.
func LogoICO() []byte { return logoICO }

// Patches returns the built-in patches folder (patches.meta.json + *.patch), or nil when the binary has none.
func Patches() fs.FS {
	sub, err := fs.Sub(patchFiles, "patches")
	if err != nil {
		return nil
	}
	if _, err := fs.Stat(sub, MetaFile); err != nil {
		return nil
	}
	return sub
}
