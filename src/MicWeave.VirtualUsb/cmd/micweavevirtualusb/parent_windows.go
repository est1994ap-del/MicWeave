package main

import (
	"context"
	"syscall"
)

func watchParent(pid int, stop context.CancelFunc) {
	handle, err := syscall.OpenProcess(syscall.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		stop()
		return
	}
	defer syscall.CloseHandle(handle)
	syscall.WaitForSingleObject(handle, syscall.INFINITE)
	stop()
}
