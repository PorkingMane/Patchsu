//go:build windows

package tui

import (
	"os/exec"
	"syscall"
)

const detachedProcess = 0x00000008

// detach starts the game without the CLI's console, so it keeps running after the CLI exits.
func detach(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: detachedProcess | syscall.CREATE_NEW_PROCESS_GROUP}
}
