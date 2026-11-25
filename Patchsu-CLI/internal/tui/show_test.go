package tui

import (
	"os"
	"testing"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/config"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/release"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// TestShowPicker prints the picker after the silent system check. Run with PATCHSU_SHOW_DATA=<data dir>.
func TestShowPicker(t *testing.T) {
	data := os.Getenv("PATCHSU_SHOW_DATA")
	if data == "" {
		t.Skip("set PATCHSU_SHOW_DATA")
	}
	t.Setenv("PATCHSU_CLI_CONFIG", t.TempDir()+"/Patchsu-CLI.json")
	src, err := release.FromDir("../../../Patchsu/patches")
	if err != nil {
		t.Fatal(err)
	}
	m := &model{opts: Options{Version: "1.0"}, info: sysinfo.Detect(), cfg: &config.Config{}, width: 110, height: 42, dataDir: data}
	_, cmd := m.Update(releaseMsg{src: src})
	t.Log("\n" + m.View())
	m.Update(cmd()) // the background check
	t.Log("\n" + m.View())
}
