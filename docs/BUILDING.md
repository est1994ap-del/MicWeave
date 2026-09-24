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
mixer's ten self-tests. It does **not** install a driver or alter audio defaults.
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
