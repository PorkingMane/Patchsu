package paths

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

func TestServerBuildsInstallSeparately(t *testing.T) {
	t.Setenv("XDG_DATA_HOME", "/data")
	t.Setenv("LOCALAPPDATA", `C:\L`)
	t.Setenv("APPDATA", `C:\R`)
	cases := []struct {
		os                   string
		name                 string
		prefix, launcher, ex string
	}{
		{"linux", "", "/data/patchsu-app", "/data/applications/patchsu.desktop", "/data/patchsu-app/Patchsu"},
		{"linux", "Patchsu-Gatari", "/data/patchsu-gatari-app", "/data/applications/patchsu-gatari.desktop", "/data/patchsu-gatari-app/Patchsu-Gatari"},
		{"windows", "Patchsu-Gatari", filepath.Join(`C:\L`, "patchsu-gatari-app"),
			filepath.Join(`C:\R`, "Microsoft", "Windows", "Start Menu", "Programs", "Patchsu-Gatari.lnk"),
			filepath.Join(`C:\L`, "patchsu-gatari-app", "Patchsu-Gatari.exe")},
		{"darwin", "Patchsu-Gatari", "/home/u/Applications/Patchsu-Gatari.app/Contents/MacOS", "",
			"/home/u/Applications/Patchsu-Gatari.app/Contents/MacOS/Patchsu-Gatari"},
	}
	for _, c := range cases {
		in := DefaultInstall(&sysinfo.Info{OS: c.os, HomeDir: "/home/u"}, c.name, "")
		if in.Prefix != c.prefix || in.Launcher != c.launcher || in.Executable != c.ex {
			t.Errorf("%s %q: got %+v", c.os, c.name, in)
		}
		if c.name == "" && in.Name != "Patchsu" {
			t.Errorf("default name %q", in.Name)
		}
	}
}

func TestInstallIn(t *testing.T) {
	t.Setenv("XDG_DATA_HOME", "/data")
	lin := &sysinfo.Info{OS: "linux", HomeDir: "/home/u"}
	in := InstallIn(lin, "Patchsu-Gatari", "/games")
	if in.Prefix != "/games/Patchsu-Gatari" || in.Executable != "/games/Patchsu-Gatari/Patchsu-Gatari" ||
		in.Launcher != "/data/applications/patchsu-gatari.desktop" {
		t.Errorf("linux: %+v", in)
	}
	mac := InstallIn(&sysinfo.Info{OS: "darwin", HomeDir: "/Users/u"}, "Patchsu", "/Applications")
	if mac.AppBundle != "/Applications/Patchsu.app" || mac.Root() != mac.AppBundle {
		t.Errorf("mac: %+v", mac)
	}
	if got := InstallIn(lin, "", ""); got != DefaultInstall(lin, "", "") {
		t.Errorf("empty parent isn't the default: %+v", got)
	}
	if got := Locate(lin, "Patchsu", "/p", "/games"); got.Prefix != "/p" {
		t.Errorf("--prefix must win: %+v", got)
	}
}

func TestOccupied(t *testing.T) {
	dir := t.TempDir()
	in := InstallIn(&sysinfo.Info{OS: "linux", HomeDir: dir}, "Patchsu", dir)
	if Occupied(in) {
		t.Fatal("missing folder counted as occupied")
	}
	must(t, os.MkdirAll(in.Prefix, 0o755))
	if Occupied(in) {
		t.Fatal("empty folder counted as occupied")
	}
	must(t, os.WriteFile(filepath.Join(in.Prefix, "notes.txt"), nil, 0o644))
	if !Occupied(in) {
		t.Fatal("someone else's folder not occupied")
	}
	must(t, os.WriteFile(in.Executable, nil, 0o755))
	if Occupied(in) {
		t.Fatal("a build-patchsu.sh build (executable, no patchsu-build.json) counted as occupied")
	}
	must(t, os.Remove(in.Executable))
	must(t, os.WriteFile(in.BuildInfo(), []byte("{}"), 0o644))
	if Occupied(in) {
		t.Fatal("a Patchsu build counted as occupied")
	}
}

func TestFindOsuData(t *testing.T) {
	home := t.TempDir()
	t.Setenv("XDG_DATA_HOME", filepath.Join(home, "share"))
	t.Setenv("APPDATA", filepath.Join(home, "Roaming"))
	lin := &sysinfo.Info{OS: "linux", HomeDir: home}
	if d := FindOsuData(lin); d.Found || d.Dir != filepath.Join(home, "share", "osu") || d.Parent != filepath.Join(home, "share") {
		t.Fatalf("linux, no osu!: %+v", d)
	}
	must(t, os.MkdirAll(filepath.Join(home, "share", "osu"), 0o755))
	if d := FindOsuData(lin); !d.Found {
		t.Fatalf("linux: %+v", d)
	}
	if d := FindOsuData(&sysinfo.Info{OS: "windows", HomeDir: home}); d.Dir != filepath.Join(home, "Roaming", "osu") {
		t.Fatalf("windows: %+v", d)
	}
	if d := FindOsuData(&sysinfo.Info{OS: "darwin", HomeDir: home}); d.Parent != filepath.Join(home, "Library", "Application Support") {
		t.Fatalf("macOS: %+v", d)
	}
}

func must(t *testing.T, err error) {
	t.Helper()
	if err != nil {
		t.Fatal(err)
	}
}
