package usbip

import (
	"encoding/binary"
	"net"
	"testing"
)

func TestOwnerTableMatchesExactClientTuple(t *testing.T) {
	table := make([]byte, 28)
	binary.LittleEndian.PutUint32(table, 1)
	row := table[4:]
	binary.LittleEndian.PutUint32(row, 5)
	copy(row[4:8], net.IPv4(127, 0, 0, 1).To4())
	binary.BigEndian.PutUint16(row[8:10], 54000)
	copy(row[12:16], net.IPv4(127, 0, 0, 1).To4())
	binary.BigEndian.PutUint16(row[16:18], 3240)
	binary.LittleEndian.PutUint32(row[20:24], 4)
	client := &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1), Port: 54000}
	server := &net.TCPAddr{IP: net.IPv4(127, 0, 0, 1), Port: 3240}
	if ownerFromTable(table, client, server) != 4 {
		t.Fatal("owner mismatch")
	}
	client.Port++
	if ownerFromTable(table, client, server) != 0 {
		t.Fatal("wrong peer accepted")
	}
	binary.LittleEndian.PutUint32(table, 0xffffffff)
	if ownerFromTable(table, client, server) != 0 {
		t.Fatal("malformed table accepted")
	}
}
