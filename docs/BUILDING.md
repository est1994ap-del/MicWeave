# Building MicWeave

## Requirements

- Windows 11 x64 and PowerShell 7.
- .NET SDK 8.0.425 exactly (selected by global.json).
- Go 1.26.8 on PATH. The module uses Go 1.22 language features.
- Network access to NuGet and the versioned upstream USBip release.
- Optional: NSIS 3.11 for a local development installer.

From the repository root:

```powershell
pwsh -NoProfile -File .\scripts\Build.ps1
```

This runs Go tests/vet, builds the helper, downloads and verifies the transport,
restores locked NuGet dependencies, publishes a self-contained app, and runs the
mixer/setup's twelve self-tests. It does **not** install a driver or alter audio defaults.
The app is placed in `artifacts/publish`; generated files are ignored by Git.
The pinned SDK selects .NET runtime 8.0.31. Move an existing `artifacts/publish`
folder aside before rebuilding so stale files cannot enter the next package.

To build a local test installer, with NSIS on PATH:

```powershell
pwsh -NoProfile -File .\scripts\Build.ps1 -DevelopmentInstaller
```

That installer is explicitly a development preview. It must not be uploaded as a
public binary until the identity and compatibility release gates are met.
The script deliberately refuses `-ReleaseInstaller` while those gates remain.
The app and installer are currently unsigned; the separately bundled USB transport
is signed. Do not disable Windows security features to install it.

To include native Intel/AMD and ARM64 versions in **one** development installer:

```powershell
pwsh -NoProfile -File .\scripts\Build.ps1 -IncludeArm64 -DevelopmentInstaller
```

The build machine is x64 Windows. This adds `artifacts/publish-arm64`, a separately
locked dependency restore, a native Go helper and the pinned signed ARM64 USBip
installer. ARM64 is cross-compiled, **not executed or hardware-tested** on x64.
The installer chooses the payload using the OS's native processor, not emulation.
See [installation and compatibility](INSTALLATION.md) for the remaining limits.

After building, test application installation in an isolated directory:

```powershell
pwsh -NoProfile -File .\scripts\Test-Installer.ps1
```

This needs Windows 11 x64, NSIS and a closed MicWeave window. It uses test-only
registration, no desktop shortcuts, no driver installation and no audio capture.
It checks blocking while running, file integrity, installed self-tests, downgrade
refusal, repair, locked-file retry and preserving unrelated files on uninstall.
Results remain under `artifacts/installer-test-*`. This does not substitute for
clean-machine driver setup/restart or ARM64 testing.

## Tests requiring the installed transport

Close other MicWeave instances. Do not run these during calls or recording.

```powershell
Start-Process .\artifacts\publish\MicWeave.exe -Wait -ArgumentList '--verify-output --output artifacts\output-test.json'
```

This attaches the local development device and sends test tones through the real
Windows microphone. It checks eight combinations of voice, two additional
sources, removal, unroute, and mute. Apps currently recording MicWeave can hear
these test tones. The test temporarily disables effects on MicWeave's endpoint,
and attempts to preserve hardware default microphones.

`--verify-live --microphone "part of microphone name"` is an optional Spotify
integration test. Without that argument it uses the default communications mic.
It may temporarily start Spotify playback and pauses it afterward if the test
started it. Use only with consent to capture the selected microphone and desktop.

`--diagnostics` captures briefly from available microphones and a program and
can record identifying metadata in its JSON report. These reports are local-only
and must be reviewed before sharing.

## Protocol fuzz test

```powershell
Push-Location src\MicWeave.VirtualUsb
go test ./internal/usbip -run '^$' -fuzz FuzzSubmitParser -fuzztime 30s -parallel 2
Pop-Location
```

The Go race detector needs a compatible C toolchain on Windows. Ordinary tests
and a short fuzz run do not substitute for longer stress tests or an independent
security review.
