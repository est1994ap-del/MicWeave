# MicWeave

Your voice and audio. One microphone.

MicWeave combines a microphone with selected program audio or other audio inputs.
Choose **MicWeave Microphone** in Discord, Zoom, recording software, or a game.
Your music keeps playing through its normal speakers or headphones.

## Current status: source preview, not a general-release installer

The mixer and Windows microphone path work on the development machine. This is
an early Windows 11 x64 project, not a certified or independently audited product.
There is no public install-and-forget release yet.

Before distributing an installer, we must resolve the USB device identity and
complete clean-computer install, restart, upgrade, and removal tests.
See [release gates](docs/RELEASING.md) and [device identity](docs/DEVICE-IDENTITY.md).
Do not redistribute development binaries using the provisional USB identifiers.

## What it does

- Pick your physical microphone from a clearly labeled menu.
- Mix one or more program, speaker-output, or audio-input sources.
- Activate, mute, and adjust each source separately, with real audio meters.
- Route or unroute program audio without muting your voice.
- Play/pause compatible programs using their Windows media controls.
- Resize, move, minimize, or cover MicWeave like a normal Windows window.
- Keep program routing off when reopening the app until you activate it.

Playback buttons cannot pause a physical microphone; they control compatible
media apps. Active/mute controls determine what enters the mix. Meter activity
alone does not prove that another app is receiving the microphone.

## How the microphone works

MicWeave does **not** need VoiceMeeter or its own newly signed kernel driver.
It still needs an existing driver: the Microsoft-signed, open-source
[usbip-win2 transport](https://github.com/vadimgrn/usbip-win2).
The app and its helper run locally. The transport presents a virtual USB device,
and Windows' built-in USB Audio driver exposes one capture endpoint.
No additional playback endpoint is created. Secure Boot need not be disabled.

First-time transport installation needs administrator permission and may need a
restart. It can temporarily reconnect USB devices, including a mouse or keyboard:
save your work first. The current installer/helper version is pinned and verified
by SHA-256, not downloaded from an unversioned "latest" URL.

## Build and test

See [BUILDING.md](docs/BUILDING.md). There are no prebuilt executables in this
source repository. Windows 11 x64, .NET 8 SDK, Go, and PowerShell 7 are required.
No driver development kit, paid certificate, or test-signing mode is required
to build the user-mode app/helper.

## Privacy and limitations

Audio stays on this computer; there is no telemetry, account, or cloud relay.
Microphone previews capture audio while active, including before program routing.
Any app allowed by Windows to record a microphone can record MicWeave's output.
MicWeave is not a boundary against malicious software running as you or an
administrator. Close it when you do not want it capturing.

The app stores device/source choices and window position in
`%LOCALAPPDATA%\MicWeave`. Logs and explicitly requested diagnostic reports may
contain device identifiers, window titles, and file paths. Review/redact them
before sharing. The public package excludes all development-machine reports.

Per-program capture may not work for protected media or apps Windows cannot
capture. Calling apps may suppress music with their own noise reduction; check
their music/original-sound settings. Audio latency and compatibility still need
broader testing. No macOS, Linux, or ARM64 release is claimed.

## License

Original MicWeave code is under the [MIT license](LICENSE). Retained upstream
code, fonts, and dependencies keep their own licenses; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

The internal `GameMusicShare` project/namespace is retained to avoid breaking
build and resource references; the app's public name is MicWeave.
