//go:build !windows

package usbip

import "net"

// Audio import is a Windows-only feature. Parser/authentication tests remain portable.
func kernelPeer(net.Conn) bool { return false }
