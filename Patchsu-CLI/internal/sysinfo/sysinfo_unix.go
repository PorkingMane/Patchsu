//go:build !windows

package sysinfo

import (
	"os"

	"golang.org/x/sys/unix"
)

func isRoot() bool { return os.Geteuid() == 0 }

func documentsDir() string { return "" }

func freeSpace(dir string) int64 {
	var st unix.Statfs_t
	if err := unix.Statfs(dir, &st); err != nil {
		return -1
	}
	return int64(st.Bavail) * int64(st.Bsize)
}
