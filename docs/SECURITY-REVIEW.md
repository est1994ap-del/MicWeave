# Local security and device-identity review

Date: 2026-09-24. This is an engineering review and test record, **not an independent
audit, certification, or promise of security**. It covers the MicWeave source/helper
and installation boundary, not all code in Windows or the shared transport driver.

## Findings addressed

| Finding | Change |
| --- | --- |
| Any local process could write PCM using a public constant header | Fresh 256-bit key per owned helper, delivered through inherited stdin; mutual HMAC-SHA256 challenge/response before PCM |
| App could adopt an unrelated listener and send it audio | Start an owned helper, require both listeners ready, authenticate the server; port collisions fail closed |
| Raw user-process USB/IP connections could import the microphone | Windows TCP peer ownership checked for the kernel/System connection; one imported session |
| Excessive transfer allocation, queued work, duplicate sequence IDs | 64 KiB transfers, 256 isochronous packets, 192 bytes per packet, 128 pending requests, duplicate rejection |
| Unbounded connection counts and slow handshakes/writes | 32 USB/IP connections, eight PCM handshake connections, two-second handshake/body/write deadlines |
| Incorrect cancellation completion and possible shutdown stalls | Serialize cancellation against completion; no completion after successful unlink; close connection before waiting for workers |
| Friendly-name matching could alter effects on a renamed unrelated mic | Require live Windows device ancestry with exact USB identity/serial before selecting or changing effects |
| Log growth | App and helper logs capped near 2 MiB; no PCM contents logged |
| Misleading reserved-device identity claim | Removed; provisional IDs documented and public installer build blocked |

The session key is not stored in a command line, environment variable, log, or
settings file. PCM is not encrypted; the sockets bind only to 127.0.0.1.
The helper exits with its owning app. The app kills only the helper process it
started. Unauthorized probes do not acquire the single PCM producer slot.

USB/IP still follows the [standard protocol](https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html).
The kernel-peer check uses Microsoft's [TCP ownership API](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable).
PID 4 ownership was observed for the installed signed transport and verified by
the real Windows audio test. This assumption needs regression testing when the
transport changes.

## Installer and device settings

The pinned USBip 0.9.8.0 x64 setup SHA-256 is
`81f426741f7ee2ed991febe24a22daca8400b6ae2f171054e3fb404897e15d39`.
The application holds the installer open read-only while hashing and launching
it, denying concurrent writes/deletion during that operation. Administrator
permission is requested only for installing the third-party transport.
The app/helper run as the current user. Existing transport binaries are located
in Program Files; the app does not search the working directory or PATH for them.

Disabling voice effects uses an undocumented Windows policy interface, limited
to the identified MicWeave capture endpoint. Compatibility is not guaranteed
across Windows updates. Defaults restoration only reverts selection of MicWeave;
it does not deliberately make MicWeave the system-wide default.

## Tests completed locally

- All Go unit tests and `go vet ./...` passed.
- Parser fuzz test: 30 seconds, 1,470,538 generated cases, no failure.
- .NET Release build: zero warnings/errors.
- Ten mixer/identity self-tests passed.
- Eight real Windows microphone checks passed after authentication/identity
  changes: simultaneous tones at 440/880/1320 Hz, source deactivation/removal,
  unroute, microphone mute, master mute, and source mute.
- Master mute captured zero signal in that test.

Personal JSON reports and machine/device identifiers are deliberately excluded
from this repository. This summary records results, not a fresh-machine test.
The public build script also passed from an independently extracted source ZIP.
The published-source payload has no machine-specific reports or prebuilt binaries.

## Remaining limits and release gates

- No assigned production USB identity yet; see [identity review](DEVICE-IDENTITY.md).
- No independent audit, Windows race-detector run, long soak test, or broad
  clean-machine test matrix has been completed.
- This does not isolate audio from other legitimate Windows recording apps.
  Programs allowed to use microphones can capture the mixed endpoint.
- Same-user malware can read/inject into ordinary user processes; administrators
  or kernel drivers can bypass these checks. Local connection-slot exhaustion
  can still deny service. No high-assurance security boundary is claimed.
- Kernel ownership alone does not prove the USB client is a particular driver
  version, nor prevent use of the shared installed driver by other authorized
  programs. A local privileged emulator can spoof USB identities.
- The third-party kernel driver has its own attack surface and update lifecycle.
  Driver signing authenticates a publisher; it is not a security audit.
- Windows device effects, protected-media capture, sleep/wake, hot-plug,
  multi-user sessions, app crashes, upgrade, and uninstall need broader testing.
- Diagnostics and exception logs may contain private paths, program titles, and
  device IDs; never upload them automatically.

## Public-package boundary

The public source tree was assembled from explicit source, artwork, font, and
license file types. It excludes the obsolete custom-driver experiment, signing
submission packages, personal screenshots/reports, installed binaries, toolchain
downloads, application settings, and machine-specific continuation notes.
Original development files remain local rather than being destructively deleted.
