package main

import (
	"bytes"
	"os"
	"path/filepath"
	"testing"
)

func TestLogRemainsBounded(t *testing.T) {
	file, err := os.Create(filepath.Join(t.TempDir(), "test.log"))
	if err != nil {
		t.Fatal(err)
	}
	defer file.Close()
	writer := &boundedLog{file: file}
	for i := 0; i < 10; i++ {
		if _, err := writer.Write(bytes.Repeat([]byte("a"), maxLogBytes/3)); err != nil {
			t.Fatal(err)
		}
		info, _ := file.Stat()
		if info.Size() > maxLogBytes {
			t.Fatal("log exceeded cap")
		}
	}
	n, err := writer.Write(bytes.Repeat([]byte("b"), maxLogBytes*2))
	if err != nil || n != maxLogBytes*2 {
		t.Fatalf("write result: %d %v", n, err)
	}
	info, _ := file.Stat()
	if info.Size() > maxLogBytes {
		t.Fatal("oversized message exceeded cap")
	}
}
