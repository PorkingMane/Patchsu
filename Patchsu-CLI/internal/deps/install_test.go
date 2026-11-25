package deps

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// Downloads the real .NET SDK (~230 MB). Run with PATCHSU_NET_TEST=<empty dir>.
func TestInstallDotnet(t *testing.T) {
	dir := os.Getenv("PATCHSU_NET_TEST")
	if dir == "" {
		t.Skip("set PATCHSU_NET_TEST to a folder to run the download test")
	}
	data := paths.Data{Root: dir}
	info := sysinfo.Detect()
	last := ""
	err := installDotnet(context.Background(), info, "10.0", data, func(msg string, d, total int64) {
		if msg != last {
			t.Log(msg)
			last = msg
		}
	})
	if err != nil {
		t.Fatal(err)
	}
	out, err := exec.Command(filepath.Join(data.Dotnet(), info.ExeName("dotnet")), "--list-sdks").Output()
	if err != nil || !strings.HasPrefix(string(out), "10.") {
		t.Fatalf("list-sdks: %q %v", out, err)
	}
	t.Log(strings.TrimSpace(string(out)))
}
