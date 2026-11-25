package paths

import (
	"os"
	"path/filepath"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// OsuData is osu!lazer's data folder (beatmaps, scores, skins) and the folder that holds it. Patchsu-CLI only puts
// folders next to it and never reads or writes inside it.
type OsuData struct {
	Dir    string // ~/.local/share/osu, %APPDATA%\osu, ~/Library/Application Support/osu
	Parent string
	Found  bool // Dir exists
}

// FindOsuData returns where osu!lazer keeps its data on this system.
func FindOsuData(info *sysinfo.Info) OsuData {
	var dir string
	switch info.OS {
	case "windows":
		dir = filepath.Join(os.Getenv("APPDATA"), "osu")
	case "darwin":
		dir = filepath.Join(info.HomeDir, "Library", "Application Support", "osu")
	default:
		dir = filepath.Join(dataHome(info), "osu")
	}
	_, err := os.Stat(dir)
	return OsuData{Dir: dir, Parent: filepath.Dir(dir), Found: err == nil}
}
