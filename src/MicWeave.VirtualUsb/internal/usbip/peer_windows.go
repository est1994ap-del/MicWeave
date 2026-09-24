package usbip

import (
	"encoding/binary"
	"net"
	"syscall"
	"unsafe"
)

var getExtendedTCPTable = syscall.NewLazyDLL("iphlpapi.dll").NewProc("GetExtendedTcpTable")

// Only Windows' kernel transport may import audio. Public device-list queries
// contain no audio and remain compatible with the installed usbip.exe client.
// Matching both addresses and ports avoids confusing the server's own PID
// with the peer. Fail closed if the Windows owner table is unavailable.
func kernelPeer(conn net.Conn) bool {
	local, ok := conn.LocalAddr().(*net.TCPAddr)
	if !ok {
		return false
	}
	remote, ok := conn.RemoteAddr().(*net.TCPAddr)
	if !ok {
		return false
	}
	var size uint32
	result, _, _ := getExtendedTCPTable.Call(0, uintptr(unsafe.Pointer(&size)), 0, 2, 5, 0)
	if result != 122 || size < 4 || size > 16*1024*1024 {
		return false
	}
	for attempt := 0; attempt < 3; attempt++ {
		table := make([]byte, size)
		result, _, _ = getExtendedTCPTable.Call(uintptr(unsafe.Pointer(&table[0])), uintptr(unsafe.Pointer(&size)), 0, 2, 5, 0)
		if result == 122 && size <= 16*1024*1024 {
			continue
		}
		if result != 0 {
			return false
		}
		return ownerFromTable(table, remote, local) == 4
	}
	return false
}

func ownerFromTable(table []byte, peer, server *net.TCPAddr) uint32 {
	if len(table) < 4 || peer.IP.To4() == nil || server.IP.To4() == nil {
		return 0
	}
	count := binary.LittleEndian.Uint32(table)
	if uint64(count)*24+4 > uint64(len(table)) {
		return 0
	}
	for i := uint32(0); i < count; i++ {
		row := table[4+int(i)*24 : 4+int(i+1)*24]
		if binary.LittleEndian.Uint32(row[0:4]) != 5 {
			continue
		}
		if net.IP(row[4:8]).Equal(peer.IP) && int(binary.BigEndian.Uint16(row[8:10])) == peer.Port &&
			net.IP(row[12:16]).Equal(server.IP) && int(binary.BigEndian.Uint16(row[16:18])) == server.Port {
			return binary.LittleEndian.Uint32(row[20:24])
		}
	}
	return 0
}
