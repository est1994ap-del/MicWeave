package main

import (
	"bytes"
	"context"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"os/signal"
	"runtime"
	"sync/atomic"
	"time"

	"virtualcables/internal/sessionauth"
	"virtualcables/internal/uac1"
	"virtualcables/internal/usbip"
)

const (
	version      = "0.1.0"
	usbipAddress = "127.0.0.1:3240"
	audioAddress = "127.0.0.1:32194"
)

func main() {
	selfTest := flag.Bool("self-test", false, "run descriptor and audio-buffer tests")
	showVersion := flag.Bool("version", false, "show version")
	usbipListen := flag.String("listen", usbipAddress, "local USB/IP listen address")
	audioListen := flag.String("audio-listen", audioAddress, "local PCM input listen address")
	bufferMS := flag.Int("buffer-ms", 160, "audio buffer capacity in milliseconds")
	parentPID := flag.Int("parent-pid", 0, "exit when the owning MicWeave process exits")
	flag.Parse()

	if *showVersion {
		fmt.Printf("MicWeave Virtual USB %s (%s/%s)\n", version, runtime.GOOS, runtime.GOARCH)
		return
	}

	logger, closeLog := newLogger()
	defer closeLog()
	device, err := uac1.NewDevice(1, *bufferMS)
	if err != nil {
		fatal(logger, err)
	}
	if *selfTest {
		if err := runSelfTest(device); err != nil {
			fatal(logger, err)
		}
		fmt.Println("MicWeave Virtual USB self-test passed.")
		return
	}
	if *parentPID <= 0 {
		fatal(logger, fmt.Errorf("start this helper through MicWeave"))
	}
	key := make([]byte, sessionauth.KeySize)
	if _, err := io.ReadFull(os.Stdin, key); err != nil {
		fatal(logger, fmt.Errorf("missing private session key"))
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	if *parentPID > 0 {
		go watchParent(*parentPID, stop)
	}
	server := usbip.NewServer(*usbipListen, []*uac1.Device{device}, logger)
	results := make(chan error, 2)
	audioReady := make(chan struct{})
	go func() { results <- server.Serve(ctx) }()
	go func() { results <- serveAudio(ctx, *audioListen, device, logger, key, audioReady) }()
	for _, ready := range []<-chan struct{}{server.Ready(), audioReady} {
		select {
		case <-ready:
		case err := <-results:
			fatal(logger, fmt.Errorf("helper startup: %v", err))
		case <-ctx.Done():
			return
		}
	}
	fmt.Println("MICWEAVE_READY_V2")

	select {
	case <-ctx.Done():
		return
	case err := <-results:
		if err != nil {
			fatal(logger, err)
		}
	}
}

func serveAudio(ctx context.Context, address string, device *uac1.Device, logger *log.Logger, key []byte, ready chan<- struct{}) error {
	host, _, err := net.SplitHostPort(address)
	if err != nil || host != "127.0.0.1" {
		return fmt.Errorf("PCM listener must use loopback")
	}
	listener, err := net.Listen("tcp", address)
	if err != nil {
		return fmt.Errorf("open MicWeave PCM input %s: %w", address, err)
	}
	defer listener.Close()
	close(ready)
	logger.Printf("PCM input listening on %s", address)
	go func() { <-ctx.Done(); _ = listener.Close() }()

	var active atomic.Bool
	slots := make(chan struct{}, 8)
	for {
		connection, err := listener.Accept()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}
		select {
		case slots <- struct{}{}:
		default:
			connection.Close()
			continue
		}
		go func(conn net.Conn) {
			defer func() { <-slots }()
			defer conn.Close()
			done := make(chan struct{})
			defer close(done)
			go func() {
				select {
				case <-ctx.Done():
					_ = conn.Close()
				case <-done:
				}
			}()
			if tcp, ok := conn.(*net.TCPConn); ok {
				_ = tcp.SetNoDelay(true)
				_ = tcp.SetKeepAlive(true)
			}
			_ = conn.SetDeadline(time.Now().Add(2 * time.Second))
			if err := sessionauth.Server(conn, key); err != nil {
				return
			}
			if !active.CompareAndSwap(false, true) {
				return
			}
			defer active.Store(false)
			_ = conn.SetDeadline(time.Time{})
			device.Buffer.Reset()
			logger.Printf("MicWeave audio stream connected")
			buffer := make([]byte, 1920)
			for {
				_ = conn.SetReadDeadline(time.Now().Add(2 * time.Second))
				count, err := io.ReadFull(conn, buffer)
				if count == len(buffer) {
					device.Feed(buffer[:count])
				}
				if err != nil {
					if err != io.EOF {
						logger.Printf("PCM stream ended: %v", err)
					}
					break
				}
			}
			device.Buffer.Reset()
			logger.Printf("MicWeave audio stream disconnected")
		}(connection)
	}
}

func runSelfTest(device *uac1.Device) error {
	if err := device.Descriptors.Validate(); err != nil {
		return err
	}
	if device.Descriptors.Product != "MicWeave Microphone" {
		return fmt.Errorf("wrong product name")
	}
	if _, status := device.HandleControl(uac1.SetupPacket{RequestType: 0, Request: uac1.RequestSetConfiguration, Value: 1}, nil); status != 0 {
		return fmt.Errorf("set configuration status %d", status)
	}
	if _, status := device.HandleControl(uac1.SetupPacket{RequestType: 1, Request: uac1.RequestSetInterface, Index: 1, Value: 1}, nil); status != 0 {
		return fmt.Errorf("set capture interface status %d", status)
	}
	want := []byte{0x12, 0x34, 0x56, 0x78}
	device.Feed(want)
	got := make([]byte, len(want))
	device.ReadCapture(got)
	if !bytes.Equal(got, want) {
		return fmt.Errorf("PCM buffer mismatch: got %x want %x", got, want)
	}
	return nil
}

func fatal(logger *log.Logger, err error) {
	logger.Printf("fatal: %v", err)
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
