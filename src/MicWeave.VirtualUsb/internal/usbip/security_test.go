package usbip

import (
	"bytes"
	"context"
	"encoding/binary"
	"io"
	"log"
	"net"
	"testing"
	"time"
	"virtualcables/internal/uac1"
)

func submitBytes(length, packets uint32, iso []IsoPacket) []byte {
	var b bytes.Buffer
	for _, v := range []any{uint32(0), length, uint32(0), packets, uint32(1), [8]byte{}} {
		binary.Write(&b, binary.BigEndian, v)
	}
	for _, p := range iso {
		binary.Write(&b, binary.BigEndian, p)
	}
	return b.Bytes()
}

func TestRejectOversizedOrMalformedRequests(t *testing.T) {
	basic := BasicHeader{Direction: DirectionIn, Endpoint: 1}
	for _, data := range [][]byte{
		submitBytes(maxTransferLength+1, NoIsoPackets, nil),
		submitBytes(192, 257, nil),
		submitBytes(192, 1, []IsoPacket{{Offset: 0, Length: 193}}),
		submitBytes(192, 1, []IsoPacket{{Offset: 0xffffffff, Length: 192}}),
		submitBytes(192, 2, []IsoPacket{{Length: 192}, {Length: 192}}),
	} {
		if _, err := readSubmit(bytes.NewReader(data), basic); err == nil {
			t.Fatal("malformed request accepted")
		}
	}
	if _, err := readSubmit(bytes.NewReader(submitBytes(192, 1, []IsoPacket{{Length: 192}})), basic); err != nil {
		t.Fatal(err)
	}
	if _, err := readSubmit(bytes.NewReader(nil), BasicHeader{Endpoint: 99}); err == nil {
		t.Fatal("invalid endpoint accepted")
	}
	if _, err := readSubmit(bytes.NewReader(nil), BasicHeader{Direction: 99}); err == nil {
		t.Fatal("invalid direction accepted")
	}
}

func TestPendingBoundDuplicateAndUnlinkSuppression(t *testing.T) {
	var output bytes.Buffer
	state := newConnectionState(&output)
	for i := 0; i < maxPendingRequests; i++ {
		if !state.addPending(uint32(i), func() {}) {
			t.Fatal("early limit")
		}
	}
	if state.addPending(0, func() {}) || state.addPending(maxPendingRequests, func() {}) {
		t.Fatal("duplicate or excess request accepted")
	}
	if !state.cancelPending(2) || state.cancelPending(2) {
		t.Fatal("unlink semantics")
	}
	if err := state.writeSubmit(SubmitRequest{Basic: BasicHeader{Sequence: 2}}, StatusConnReset, 0, nil, nil, 0); err != nil {
		t.Fatal(err)
	}
	if output.Len() != 0 {
		t.Fatal("unlinked request produced forbidden completion")
	}
	if err := state.writeSubmit(SubmitRequest{Basic: BasicHeader{Sequence: 3}}, StatusOK, 0, nil, nil, 0); err != nil {
		t.Fatal(err)
	}
	if state.cancelPending(3) {
		t.Fatal("already completed request unlinked")
	}
}

func TestRawUserProcessCannotImportMicrophone(t *testing.T) {
	device, _ := uac1.NewDevice(1, 100)
	server := NewServer("127.0.0.1:0", []*uac1.Device{device}, log.New(io.Discard, "", 0))
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan error, 1)
	go func() { done <- server.Serve(ctx) }()
	select {
	case <-server.Ready():
	case <-time.After(time.Second):
		t.Fatal("server not ready")
	}
	server.mu.Lock()
	address := server.listener.Addr().String()
	server.mu.Unlock()
	conn, err := net.DialTimeout("tcp", address, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	defer conn.Close()
	conn.SetDeadline(time.Now().Add(time.Second))
	writeOpHeader(conn, OpReqImport, 0)
	h, err := readOpHeader(conn)
	// Reject is status=1; readOpHeader itself rejects nonzero status.
	if h.Code != OpRepImport || h.Status != 1 || err == nil {
		t.Fatalf("raw import not rejected: %+v %v", h, err)
	}
	cancel()
	if err := <-done; err != nil {
		t.Fatal(err)
	}
}

func TestNonLoopbackBindingRejected(t *testing.T) {
	server := NewServer("0.0.0.0:0", nil, log.New(io.Discard, "", 0))
	if err := server.Serve(context.Background()); err == nil {
		t.Fatal("public listener accepted")
	}
}

func FuzzSubmitParser(f *testing.F) {
	f.Add(submitBytes(192, 1, []IsoPacket{{Length: 192}}))
	f.Add(submitBytes(0xffffffff, 0xffffffff, nil))
	f.Fuzz(func(t *testing.T, data []byte) {
		if len(data) > 128*1024 {
			return
		}
		parsed, err := readSubmit(bytes.NewReader(data), BasicHeader{Direction: DirectionIn, Endpoint: 1})
		if err == nil && (parsed.req.TransferBufferLength > maxTransferLength || len(parsed.packets) > 256) {
			t.Fatal("bounds bypass")
		}
	})
}
