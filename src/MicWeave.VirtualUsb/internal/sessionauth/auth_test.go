package sessionauth

import (
	"bytes"
	"crypto/hmac"
	"io"
	"net"
	"testing"
	"time"
)

func TestMutualAuthenticationAndWrongKey(t *testing.T) {
	for _, correct := range []bool{true, false} {
		server, client := net.Pipe()
		server.SetDeadline(time.Now().Add(time.Second))
		client.SetDeadline(time.Now().Add(time.Second))
		key := bytes.Repeat([]byte{7}, KeySize)
		done := make(chan error, 1)
		go func() { done <- Server(server, key); server.Close() }()
		hello := make([]byte, len(Magic)+32)
		if _, err := io.ReadFull(client, hello); err != nil {
			t.Fatal(err)
		}
		if string(hello[:len(Magic)]) != Magic {
			t.Fatal("wrong protocol")
		}
		usedKey := append([]byte(nil), key...)
		if !correct {
			usedKey[0] ^= 1
		}
		clientNonce := bytes.Repeat([]byte{9}, 32)
		client.Write(append(clientNonce, Proof(usedKey, "client", hello[len(Magic):], clientNonce)...))
		serverProof := make([]byte, 32)
		_, readErr := io.ReadFull(client, serverProof)
		authErr := <-done
		if correct {
			if readErr != nil || authErr != nil || !hmac.Equal(serverProof, Proof(key, "server", hello[len(Magic):], clientNonce)) {
				t.Fatal("mutual authentication failed")
			}
		} else if authErr == nil || readErr == nil {
			t.Fatal("wrong key accepted")
		}
		client.Close()
	}
}

func TestProofBindsRoleAndBothNonces(t *testing.T) {
	key := bytes.Repeat([]byte{1}, 32)
	a := bytes.Repeat([]byte{2}, 32)
	b := bytes.Repeat([]byte{3}, 32)
	original := Proof(key, "client", a, b)
	if hmac.Equal(original, Proof(key, "server", a, b)) {
		t.Fatal("role reflection")
	}
	a[0]++
	if hmac.Equal(original, Proof(key, "client", a, b)) {
		t.Fatal("server nonce not bound")
	}
	a[0]--
	b[0]++
	if hmac.Equal(original, Proof(key, "client", a, b)) {
		t.Fatal("client nonce not bound")
	}
}
