package build

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/bundle"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/manifest"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/paths"
	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

const description = "osu!lazer that never submits scores; local profiles on right-click; shares the official osu! data"

// describe is the launcher comment: the plain Patchsu text, or which server a Patchsu-<Server> build is for.
func (b *Builder) describe() string {
	if sv := b.Source.Manifest.ServerOf(b.Selection); sv != nil {
		return "osu!lazer with Patchsu patches for the " + sv.Name + " server"
	}
	return description
}

func (b *Builder) install(ctx context.Context, line func(string)) error {
	inst := b.Install
	if inst.Name == "" {
		inst.Name = manifest.BaseName
	}
	built := filepath.Join(inst.Prefix, b.Info.ExeName("osu!"))
	if _, err := os.Stat(built); err != nil {
		return fmt.Errorf("the build finished but %s was not found", built)
	}
	// The game is called Patchsu (or Patchsu-<Server>): rename the launcher (it finds osu!.dll next to it on its own).
	os.Remove(inst.Executable)
	if err := os.Rename(built, inst.Executable); err != nil {
		return err
	}
	line("Game: " + inst.Executable)

	switch b.Info.OS {
	case "linux":
		icon := filepath.Join(inst.Prefix, "patchsu-icon.png")
		if err := os.WriteFile(icon, bundle.LogoPNG(), 0o644); err != nil {
			line("Warning: no menu icon (" + err.Error() + ")")
		}
		if b.Launcher {
			if err := writeDesktopFile(inst, icon, b.describe()); err != nil {
				return fmt.Errorf("creating the menu entry: %w", err)
			}
			line("Menu entry: " + inst.Launcher)
		}
	case "windows":
		if b.Launcher {
			if err := b.exec(ctx, inst.Prefix, line, "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
				shortcutScript(inst, b.describe())); err != nil {
				line("Warning: could not create the Start Menu shortcut. Start the game from " + inst.Executable)
			} else {
				line("Start Menu shortcut: " + inst.Name)
			}
		}
	case "darwin":
		if err := b.macBundle(ctx, inst, line); err != nil {
			return err
		}
	}
	return b.writeBuildInfo()
}

// desktopQuote wraps a path for Exec=: double quotes, with \ " ` $ escaped.
func desktopQuote(s string) string {
	r := strings.NewReplacer(`\`, `\\`, `"`, `\"`, "`", "\\`", `$`, `\$`)
	return `"` + r.Replace(s) + `"`
}

func writeDesktopFile(inst paths.Install, icon, comment string) error {
	if err := os.MkdirAll(filepath.Dir(inst.Launcher), 0o755); err != nil {
		return err
	}
	content := fmt.Sprintf(`[Desktop Entry]
Type=Application
Version=1.0
Name=%s
GenericName=Rhythm game (osu!lazer, local-priority)
Comment=%s
Exec=%s
Path=%s
Icon=%s
Terminal=false
Categories=Game;
StartupNotify=true
StartupWMClass=%s
`, inst.Name, comment, desktopQuote(inst.Executable), inst.Prefix, icon, inst.Name)
	if err := os.WriteFile(inst.Launcher, []byte(content), 0o755); err != nil {
		return err
	}
	if p, err := exec.LookPath("update-desktop-database"); err == nil {
		_ = exec.Command(p, filepath.Dir(inst.Launcher)).Run()
	}
	return nil
}

func psQuote(s string) string { return "'" + strings.ReplaceAll(s, "'", "''") + "'" }

func shortcutScript(inst paths.Install, comment string) string {
	return "$s = (New-Object -ComObject WScript.Shell).CreateShortcut(" + psQuote(inst.Launcher) + ");" +
		"$s.TargetPath = " + psQuote(inst.Executable) + ";" +
		"$s.WorkingDirectory = " + psQuote(inst.Prefix) + ";" +
		"$s.IconLocation = " + psQuote(inst.Executable+",0") + ";" +
		"$s.Description = " + psQuote(comment) + ";" +
		"$s.Save()"
}

// infoPlist: %[1]s is the app name ("Patchsu" or "Patchsu-<Server>"), %[2]s its lower-case slug.
const infoPlist = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>%[1]s</string>
	<key>CFBundleDisplayName</key><string>%[1]s</string>
	<key>CFBundleIdentifier</key><string>io.github.porkingmane.%[2]s</string>
	<key>CFBundleExecutable</key><string>%[1]s</string>
	<key>CFBundleIconFile</key><string>%[1]s</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleShortVersionString</key><string>1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>LSMinimumSystemVersion</key><string>12.0</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>LSApplicationCategoryType</key><string>public.app-category.music-games</string>
</dict>
</plist>
`

// macBundle turns the publish output into a .app: Info.plist, an icon made from the Patchsu logo (sips + iconutil)
// and an ad-hoc signature, without which Apple Silicon refuses to start the binary.
func (b *Builder) macBundle(ctx context.Context, inst paths.Install, line func(string)) error {
	app := inst.AppBundle
	contents := filepath.Join(app, "Contents")
	plist := fmt.Sprintf(infoPlist, inst.Name, paths.Slug(inst.Name))
	if err := os.WriteFile(filepath.Join(contents, "Info.plist"), []byte(plist), 0o644); err != nil {
		return err
	}
	res := filepath.Join(contents, "Resources")
	if err := os.MkdirAll(res, 0o755); err != nil {
		return err
	}
	if err := makeIcns(ctx, bundle.LogoPNG(), filepath.Join(res, inst.Name+".icns")); err != nil {
		line("Warning: no app icon (" + err.Error() + ")")
	}
	if err := b.exec(ctx, app, line, "codesign", "--force", "--deep", "--sign", "-", app); err != nil {
		return fmt.Errorf("signing %s.app: %w", inst.Name, err)
	}
	line("App: " + app)
	return nil
}

func makeIcns(ctx context.Context, logo []byte, icns string) error {
	set, err := os.MkdirTemp("", "patchsu-*.iconset")
	if err != nil {
		return err
	}
	defer os.RemoveAll(set)
	png := filepath.Join(set, "..", filepath.Base(set)+"-logo.png")
	if err := os.WriteFile(png, logo, 0o644); err != nil {
		return err
	}
	defer os.Remove(png)
	for _, size := range []int{16, 32, 128, 256, 512} {
		for _, scale := range []int{1, 2} {
			px := size * scale
			name := fmt.Sprintf("icon_%dx%d", size, size)
			if scale == 2 {
				name += "@2x"
			}
			out := filepath.Join(set, name+".png")
			if err := exec.CommandContext(ctx, "sips", "-z", fmt.Sprint(px), fmt.Sprint(px), png, "--out", out).Run(); err != nil {
				return fmt.Errorf("sips: %w", err)
			}
		}
	}
	return exec.CommandContext(ctx, "iconutil", "-c", "icns", set, "-o", icns).Run()
}

// Uninstall removes the installed game and its launcher. The osu! data folder is never touched.
func Uninstall(info *sysinfo.Info, inst paths.Install) ([]string, error) {
	var removed []string
	target := inst.Prefix
	if inst.AppBundle != "" {
		target = inst.AppBundle
	}
	for _, p := range []string{target, inst.Launcher} {
		if p == "" {
			continue
		}
		if _, err := os.Lstat(p); err != nil {
			continue
		}
		if err := os.RemoveAll(p); err != nil {
			return removed, err
		}
		removed = append(removed, p)
	}
	if info.OS == "linux" && inst.Launcher != "" {
		if p, err := exec.LookPath("update-desktop-database"); err == nil {
			_ = exec.Command(p, filepath.Dir(inst.Launcher)).Run()
		}
	}
	return removed, nil
}
