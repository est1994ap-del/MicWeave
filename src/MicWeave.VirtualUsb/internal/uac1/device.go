// Portions derived from Virtual Cables, copyright (c) 2026 Tarek Wasfy and AI.
// Licensed under the BSD 2-Clause License; see LICENSE.Virtual-Cables.txt.
package uac1

import (
	"encoding/binary"
	"fmt"
	"math"
	"sync"

	"virtualcables/internal/audio"
)

const (
	RequestGetStatus        = 0x00
	RequestClearFeature     = 0x01
	RequestSetFeature       = 0x03
	RequestSetAddress       = 0x05
	RequestGetDescriptor    = 0x06
	RequestSetDescriptor    = 0x07
	RequestGetConfiguration = 0x08
	RequestSetConfiguration = 0x09
	RequestGetInterface     = 0x0A
	RequestSetInterface     = 0x0B
	RequestSynchFrame       = 0x0C
	AudioSetCur             = 0x01
	AudioGetCur             = 0x81
	AudioGetMin             = 0x82
	AudioGetMax             = 0x83
	AudioGetRes             = 0x84
)

type SetupPacket struct {
	RequestType uint8
	Request     uint8
	Value       uint16
	Index       uint16
	Length      uint16
}

func ParseSetup(b []byte) (SetupPacket, error) {
	if len(b) < 8 {
		return SetupPacket{}, fmt.Errorf("setup packet too short")
	}
	return SetupPacket{
		RequestType: b[0], Request: b[1],
		Value:  binary.LittleEndian.Uint16(b[2:4]),
		Index:  binary.LittleEndian.Uint16(b[4:6]),
		Length: binary.LittleEndian.Uint16(b[6:8]),
	}, nil
}

type Device struct {
	Number      int
	BusID       string
	Descriptors *Descriptors
	Buffer      *audio.Ring

	mu            sync.Mutex
	configuration uint8
	altSetting    map[uint8]uint8
	sampleRate    uint32
	mute          bool
	volume        int16
}

func NewDevice(number, latencyMS int) (*Device, error) {
	descriptors, err := NewDescriptors(number)
	if err != nil {
		return nil, err
	}
	if latencyMS < 20 {
		latencyMS = 20
	}
	if latencyMS > 1000 {
		latencyMS = 1000
	}
	capacity := 48000 * 2 * 2 * latencyMS / 1000
	return &Device{
		Number: 1, BusID: "1-1", Descriptors: descriptors,
		Buffer: audio.NewRing(capacity), altSetting: map[uint8]uint8{0: 0, 1: 0},
		sampleRate: 48000,
	}, nil
}

func (d *Device) Product() string { return d.Descriptors.Product }

// Feed adds MicWeave's final PCM16 stereo mix to the virtual microphone.
func (d *Device) Feed(p []byte) int { return d.Buffer.Write(p) }

func (d *Device) HandleControl(setup SetupPacket, out []byte) (data []byte, status int32) {
	d.mu.Lock()
	defer d.mu.Unlock()

	if setup.RequestType&0x60 == 0 {
		switch setup.Request {
		case RequestGetDescriptor:
			value, ok := d.Descriptors.GetDescriptor(uint8(setup.Value>>8), uint8(setup.Value))
			if !ok {
				return nil, -32
			}
			return truncate(value, setup.Length), 0
		case RequestSetAddress:
			return nil, 0
		case RequestSetConfiguration:
			configuration := uint8(setup.Value)
			if configuration > 1 {
				return nil, -32
			}
			d.configuration = configuration
			d.altSetting[0], d.altSetting[1] = 0, 0
			d.Buffer.Reset()
			return nil, 0
		case RequestGetConfiguration:
			return truncate([]byte{d.configuration}, setup.Length), 0
		case RequestSetInterface:
			iface, alt := uint8(setup.Index), uint8(setup.Value)
			if d.configuration != 1 || iface > 1 || alt > 1 || (iface == 0 && alt != 0) {
				return nil, -32
			}
			d.altSetting[iface] = alt
			if iface == 1 && alt == 0 {
				d.Buffer.Reset()
			}
			return nil, 0
		case RequestGetInterface:
			alt, ok := d.altSetting[uint8(setup.Index)]
			if !ok {
				return nil, -32
			}
			return truncate([]byte{alt}, setup.Length), 0
		case RequestGetStatus:
			return truncate([]byte{0, 0}, setup.Length), 0
		case RequestSynchFrame:
			return truncate([]byte{0, 0}, setup.Length), 0
		case RequestClearFeature, RequestSetFeature:
			return nil, 0
		default:
			return nil, -32
		}
	}

	recipient := setup.RequestType & 0x1f
	selector := uint8(setup.Value >> 8)
	endpoint := uint8(setup.Index)
	if recipient == 0x02 && selector == 0x01 && endpoint == 0x81 {
		switch setup.Request {
		case AudioSetCur:
			if len(out) >= 3 {
				rate := uint32(out[0]) | uint32(out[1])<<8 | uint32(out[2])<<16
				if rate != 48000 {
					return nil, -32
				}
				d.sampleRate = rate
			}
			return nil, 0
		case AudioGetCur:
			return truncate(rate24(d.sampleRate), setup.Length), 0
		case AudioGetMin, AudioGetMax:
			return truncate(rate24(48000), setup.Length), 0
		case AudioGetRes:
			return truncate(rate24(1), setup.Length), 0
		}
	}

	entityID, interfaceNumber := uint8(setup.Index>>8), uint8(setup.Index)
	if recipient == 0x01 && interfaceNumber == 0 && entityID == 2 {
		switch selector {
		case 0x01:
			switch setup.Request {
			case AudioSetCur:
				if len(out) > 0 {
					d.mute = out[0] != 0
				}
				return nil, 0
			case AudioGetCur:
				if d.mute {
					return truncate([]byte{1}, setup.Length), 0
				}
				return truncate([]byte{0}, setup.Length), 0
			}
		case 0x02:
			switch setup.Request {
			case AudioSetCur:
				if len(out) >= 2 {
					d.volume = int16(binary.LittleEndian.Uint16(out[:2]))
					if d.volume > 0 {
						d.volume = 0
					}
					if d.volume < -60*256 {
						d.volume = -60 * 256
					}
				}
				return nil, 0
			case AudioGetCur:
				return truncate(int16LE(d.volume), setup.Length), 0
			case AudioGetMin:
				return truncate(int16LE(-60*256), setup.Length), 0
			case AudioGetMax:
				return truncate(int16LE(0), setup.Length), 0
			case AudioGetRes:
				return truncate(int16LE(256), setup.Length), 0
			}
		}
	}
	return nil, -32
}

func (d *Device) CaptureActive() bool {
	d.mu.Lock()
	defer d.mu.Unlock()
	return d.configuration == 1 && d.altSetting[1] == 1
}

func (d *Device) ReadCapture(p []byte) int {
	if !d.CaptureActive() {
		clear(p)
		return 0
	}
	count := d.Buffer.ReadSilence(p)
	d.mu.Lock()
	mute, volume := d.mute, d.volume
	d.mu.Unlock()
	if mute {
		clear(p)
		return count
	}
	if volume != 0 {
		gain := math.Pow(10, float64(volume)/(256*20))
		for i := 0; i+1 < len(p); i += 2 {
			sample := int16(binary.LittleEndian.Uint16(p[i : i+2]))
			binary.LittleEndian.PutUint16(p[i:i+2], uint16(int16(float64(sample)*gain)))
		}
	}
	return count
}

func truncate(b []byte, n uint16) []byte {
	if int(n) < len(b) {
		return b[:n]
	}
	return b
}
func rate24(rate uint32) []byte { return []byte{byte(rate), byte(rate >> 8), byte(rate >> 16)} }
func int16LE(value int16) []byte {
	b := make([]byte, 2)
	binary.LittleEndian.PutUint16(b, uint16(value))
	return b
}
