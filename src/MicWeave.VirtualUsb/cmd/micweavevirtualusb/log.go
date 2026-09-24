package main

import (
	"io"
	"log"
	"os"
	"path/filepath"
	"sync"
)

const maxLogBytes = 2 * 1024 * 1024

type boundedLog struct {
	mu   sync.Mutex
	file *os.File
	size int64
}

func (w *boundedLog) Write(p []byte) (int, error) {
	w.mu.Lock()
	defer w.mu.Unlock()
	original := len(p)
	if len(p) > maxLogBytes {
		p = p[len(p)-maxLogBytes:]
	}
	if w.size+int64(len(p)) > maxLogBytes {
		if err := w.file.Truncate(0); err != nil {
			return 0, err
		}
		if _, err := w.file.Seek(0, io.SeekStart); err != nil {
			return 0, err
		}
		w.size = 0
	}
	n, err := w.file.Write(p)
	w.size += int64(n)
	if err != nil {
		return n, err
	}
	return original, nil
}

func newLogger() (*log.Logger, func()) {
	root := os.Getenv("LOCALAPPDATA")
	if root == "" {
		root = os.TempDir()
	}
	directory := filepath.Join(root, "MicWeave", "Logs")
	_ = os.MkdirAll(directory, 0o700)
	file, err := os.OpenFile(filepath.Join(directory, "VirtualUsb.log"), os.O_CREATE|os.O_WRONLY, 0o600)
	if err != nil {
		return log.New(io.Discard, "", 0), func() {}
	}
	size, _ := file.Seek(0, io.SeekEnd)
	writer := &boundedLog{file: file, size: size}
	return log.New(writer, "", log.Ldate|log.Ltime|log.Lmicroseconds), func() {
		writer.mu.Lock()
		defer writer.mu.Unlock()
		_ = file.Close()
	}
}
