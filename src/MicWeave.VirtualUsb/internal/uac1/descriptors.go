// Portions derived from Virtual Cables, copyright (c) 2026 Tarek Wasfy and AI.
// Licensed under the BSD 2-Clause License; see LICENSE.Virtual-Cables.txt.
package uac1

import (
	"encoding/binary"
	"fmt"
	"unicode/utf16"
)

const (
	DescriptorDevice        = 0x01
	DescriptorConfiguration = 0x02
	DescriptorString        = 0x03
	DescriptorInterface     = 0x04
	DescriptorEndpoint      = 0x05
	DescriptorCSInterface   = 0x24
	DescriptorCSEndpoint    = 0x25
)

// Descriptors exposes one capture-only USB Audio Class 1 device. There is no
// playback interface, so Windows creates one understandable microphone and no
// extra output endpoint.
type Descriptors struct {
	Product string
	Serial  string
	Device  []byte
	Config  []byte
	Strings map[uint8][]byte
}

func NewDescriptors(_ int) (*Descriptors, error) {
	d := &Descriptors{
		Product: "MicWeave Microphone",
		Serial:  "MICWEAVE-AUDIO-001",
		Strings: make(map[uint8][]byte),
	}
	d.Device = deviceDescriptor()
	d.Config = configurationDescriptor()
	d.Strings[0] = []byte{4, DescriptorString, 0x09, 0x04}
	d.Strings[1] = stringDescriptor("MicWeave")
	d.Strings[2] = stringDescriptor(d.Product)
	d.Strings[3] = stringDescriptor(d.Serial)
	if err := d.Validate(); err != nil {
		return nil, err
	}
	return d, nil
}

func deviceDescriptor() []byte {
	b := make([]byte, 18)
	b[0] = 18
	b[1] = DescriptorDevice
	binary.LittleEndian.PutUint16(b[2:4], 0x0110)
	b[4], b[5], b[6] = 0, 0, 0
	b[7] = 64
	// Development-only identity, NOT an assigned VID or a reserved MicWeave PID.
	// Public binary distribution is gated on an approved allocation; see
	// docs/DEVICE-IDENTITY.md. Never substitute another vendor's identity.
	binary.LittleEndian.PutUint16(b[8:10], 0xFFFF)
	binary.LittleEndian.PutUint16(b[10:12], 0x4D57)
	binary.LittleEndian.PutUint16(b[12:14], 0x0100)
	b[14], b[15], b[16], b[17] = 1, 2, 3, 1
	return b
}

func configurationDescriptor() []byte {
	var b []byte
	appendBytes := func(x ...byte) { b = append(b, x...) }

	// One AudioControl interface and one capture AudioStreaming interface.
	appendBytes(9, DescriptorConfiguration, 0, 0, 2, 1, 0, 0x80, 50)

	// AudioControl interface 0.
	appendBytes(9, DescriptorInterface, 0, 0, 0, 0x01, 0x01, 0x00, 0)
	// UAC1 header: class-specific AC block is 40 bytes, one streaming interface.
	appendBytes(9, DescriptorCSInterface, 0x01, 0x00, 0x01, 40, 0, 1, 1)
	// Stereo microphone input terminal -> mute/volume feature -> USB stream.
	appendBytes(12, DescriptorCSInterface, 0x02, 1, 0x01, 0x02, 0, 2, 0x03, 0x00, 0, 0)
	appendBytes(10, DescriptorCSInterface, 0x06, 2, 1, 1, 0x03, 0x00, 0x00, 0)
	appendBytes(9, DescriptorCSInterface, 0x03, 3, 0x01, 0x01, 0, 2, 0)

	// Capture interface 1: alternate 0 is idle; alternate 1 streams PCM.
	appendBytes(9, DescriptorInterface, 1, 0, 0, 0x01, 0x02, 0x00, 0)
	appendBytes(9, DescriptorInterface, 1, 1, 1, 0x01, 0x02, 0x00, 0)
	appendBytes(7, DescriptorCSInterface, 0x01, 3, 1, 0x01, 0x00)
	appendBytes(11, DescriptorCSInterface, 0x02, 1, 2, 2, 16, 1, 0x80, 0xBB, 0x00)
	// 48 frames/ms * 2 channels * 2 bytes = 192 bytes per USB frame.
	appendBytes(9, DescriptorEndpoint, 0x81, 0x0D, 0xC0, 0x00, 1, 0, 0)
	appendBytes(7, DescriptorCSEndpoint, 0x01, 0, 0, 0, 0)

	binary.LittleEndian.PutUint16(b[2:4], uint16(len(b)))
	return b
}

func stringDescriptor(s string) []byte {
	encoded := utf16.Encode([]rune(s))
	if len(encoded) > 126 {
		encoded = encoded[:126]
	}
	b := make([]byte, 2+len(encoded)*2)
	b[0], b[1] = byte(len(b)), DescriptorString
	for i, value := range encoded {
		binary.LittleEndian.PutUint16(b[2+i*2:], value)
	}
	return b
}

func (d *Descriptors) GetDescriptor(descriptorType, index uint8) ([]byte, bool) {
	switch descriptorType {
	case DescriptorDevice:
		if index == 0 {
			return append([]byte(nil), d.Device...), true
		}
	case DescriptorConfiguration:
		if index == 0 {
			return append([]byte(nil), d.Config...), true
		}
	case DescriptorString:
		value, ok := d.Strings[index]
		return append([]byte(nil), value...), ok
	}
	return nil, false
}

func (d *Descriptors) Validate() error {
	if len(d.Device) != 18 || d.Device[0] != 18 || d.Device[1] != DescriptorDevice {
		return fmt.Errorf("invalid device descriptor")
	}
	if len(d.Config) < 9 || d.Config[1] != DescriptorConfiguration || d.Config[4] != 2 {
		return fmt.Errorf("invalid capture-only configuration descriptor")
	}
	if got := int(binary.LittleEndian.Uint16(d.Config[2:4])); got != len(d.Config) {
		return fmt.Errorf("configuration wTotalLength=%d, actual=%d", got, len(d.Config))
	}
	endpointCount := 0
	for pos := 0; pos < len(d.Config); {
		length := int(d.Config[pos])
		if length < 2 || pos+length > len(d.Config) {
			return fmt.Errorf("invalid descriptor at offset %d", pos)
		}
		if d.Config[pos+1] == DescriptorEndpoint {
			endpointCount++
			if length < 7 || d.Config[pos+2] != 0x81 {
				return fmt.Errorf("unexpected non-capture endpoint")
			}
		}
		pos += length
	}
	if endpointCount != 1 {
		return fmt.Errorf("capture-only device must expose exactly one endpoint")
	}
	return nil
}
