// Package sessionauth authenticates both ends before any PCM leaves MicWeave.
// The 256-bit key is supplied to the owned helper through inherited stdin,
// never a command line, environment variable, disk file, or network message.
package sessionauth

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"errors"
	"io"
)

const Magic = "MICWEAVE_PCM_V2\n"
const KeySize = 32

func Proof(key []byte, role string, serverNonce, clientNonce []byte) []byte {
	mac := hmac.New(sha256.New, key)
	mac.Write([]byte("MicWeave/2/" + role))
	mac.Write(serverNonce)
	mac.Write(clientNonce)
	return mac.Sum(nil)
}

func Server(stream io.ReadWriter, key []byte) error {
	if len(key) != KeySize {
		return errors.New("invalid session key length")
	}
	nonce := make([]byte, 32)
	if _, err := rand.Read(nonce); err != nil {
		return err
	}
	if _, err := stream.Write(append([]byte(Magic), nonce...)); err != nil {
		return err
	}
	response := make([]byte, 64)
	if _, err := io.ReadFull(stream, response); err != nil {
		return err
	}
	if !hmac.Equal(response[32:], Proof(key, "client", nonce, response[:32])) {
		return errors.New("PCM client authentication rejected")
	}
	_, err := stream.Write(Proof(key, "server", nonce, response[:32]))
	return err
}
