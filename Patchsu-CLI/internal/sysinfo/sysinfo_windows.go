//go:build windows

package sysinfo

import (
	"golang.org/x/sys/windows"
)

// On Windows the CLI never needs admin rights (git is portable MinGit), so this only matters for display.
func isRoot() bool { return windows.GetCurrentProcessToken().IsElevated() }

// documentsDir asks Windows for the real Documents folder, which may be moved or inside OneDrive.
func documentsDir() string {
	d, err := windows.KnownFolderPath(windows.FOLDERID_Documents, 0)
	if err != nil {
		return ""
	}
	return d
}

func freeSpace(dir string) int64 {
	p, err := windows.UTF16PtrFromString(dir)
	if err != nil {
		return -1
	}
	var free uint64
	if err := windows.GetDiskFreeSpaceEx(p, &free, nil, nil); err != nil {
		return -1
	}
	return int64(free)
}
