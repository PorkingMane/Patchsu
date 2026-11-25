//go:build !windows

package tui

import (
	"os/exec"
	"syscall"
)

// detach starts the game in its own session, so it keeps running after the CLI exits.
func detach(cmd *exec.Cmd) { cmd.SysProcAttr = &syscall.SysProcAttr{Setsid: true} }
