// Package config stores the CLI's remembered choices in Patchsu-CLI.json in the user config folder.
package config

import (
	"encoding/json"
	"os"
	"path/filepath"

	"github.com/PorkingMane/Patchsu/Patchsu-CLI/internal/sysinfo"
)

// Config is what the CLI remembers between runs.
type Config struct {
	DataDir string   `json:"dataDir,omitempty"`
	Patches []string `json:"patches,omitempty"` // last selection
	Preset  string   `json:"preset,omitempty"`  // preset the last selection came from
	Server  string   `json:"server,omitempty"`  // last custom server chosen for the "Custom server" preset
	// InstallDir is the folder the game goes into (<InstallDir>/Patchsu, Patchsu.app on macOS); "" = the standard place.
	InstallDir string `json:"installDir,omitempty"`
}

// Path is the config file location. PATCHSU_CLI_CONFIG overrides it (tests use it to keep the real file untouched).
func Path() string {
	if p := os.Getenv("PATCHSU_CLI_CONFIG"); p != "" {
		return p
	}
	return filepath.Join(sysinfo.ConfigDir(), "Patchsu-CLI.json")
}

// Load reads the config. A missing or broken file gives an empty config.
func Load() *Config {
	c := &Config{}
	if data, err := os.ReadFile(Path()); err == nil {
		_ = json.Unmarshal(data, c)
	}
	return c
}

// Save writes the config.
func (c *Config) Save() error {
	if err := os.MkdirAll(filepath.Dir(Path()), 0o755); err != nil {
		return err
	}
	data, err := json.MarshalIndent(c, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(Path(), data, 0o644)
}
