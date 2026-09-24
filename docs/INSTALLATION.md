# Installation and Windows compatibility

## The intended experience

Download one MicWeave installer, install it, and open the desktop shortcut. The
installer selects the native Intel/AMD (x64) or ARM64 app for the PC. The app
includes its .NET runtime; users do not need developer tools or VoiceMeeter.

On first use, approve installation of the bundled USBip component if it is
missing. Save work and stop transfers/calls first: USB devices can briefly
disconnect, and a Windows restart may be required. Reopen MicWeave afterward.
Choose your physical microphone and audio sources, then select **MicWeave
Microphone** in the receiving app. Your regular playback device stays in use.

This experience is implemented in the **development installer**, not yet
validated as a general public release. There is no public installer download yet.

## No new MicWeave driver to sign

MicWeave bundles the unmodified, already-signed
[usbip-win2 0.9.8.0 component](https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.0).
Windows supplies its [USB audio class driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/supported-usb-classes).
Neither the maintainer nor people installing MicWeave need to submit a new
MicWeave kernel driver to Microsoft or buy a driver-signing certificate for this
design. This is **not driverless**: it reuses an existing signed driver.

Do not rebuild or modify the transport's signed driver package. Builds verify
the upstream installer's signature and exact file hash separately for each
processor. Runtime setup verifies the bundled installer's hash before elevation.
Existing installations are reused; a connection error does not trigger repeated
driver reinstallation. The transport is shared with other applications and is
not removed by MicWeave's uninstaller.

## What is and is not verified

| Area | Current evidence |
| --- | --- |
| Intel/AMD Windows 11 | Builds and automated mixer/setup checks pass on the development PC; actual microphone-path checks have passed there |
| ARM-based Windows 11 | Native app/helper builds and signed ARM64 transport packaging are implemented; native hardware testing is still pending |
| One installer | Detects native processor; selects only its matching payload; an x64-only package refuses ARM64 rather than installing a wrong driver |
| Secure Boot | Enabled on the development PC; installer never changes it or enables test signing |
| Clean PCs, Memory Integrity, different hardware | Still require a real compatibility test matrix; development-PC success is not proof |
| VoiceMeeter and development tools | Not required by end users; .NET runtime and USBip installer are bundled |

Ordinary Windows audio inputs, including USB microphones, headsets and audio
interfaces, are selectable if their manufacturer driver exposes them to Windows
audio. This does not promise support for broken/disconnected devices, ASIO-only
inputs, exclusive-use conflicts, or capture-protected content. Bluetooth devices
also need testing in their actual microphone/playback modes.

## Windows protections still apply

Administrator approval is needed for the shared driver component, not for normal
mixing. Work/school administrators can prohibit third-party drivers or apps.
Windows microphone privacy permissions must allow desktop apps to use the mic.

The MicWeave app/helper/installer are currently unsigned. This is separate from
the already-signed driver. [Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview)
can block unknown unsigned applications. A warning is not always a warning with
a bypass button. Do not instruct users to disable protections. Consequently,
**“works on every Windows 11 PC in any configuration” is not a verified or
supportable promise**, even with an already-signed driver.

[SignPath Foundation](https://signpath.org/terms.html) is a possible no-fee route
for signing the application and installer, subject to its project/reputation,
release, review and account-security requirements. MicWeave has not been accepted;
do not claim a certificate, guaranteed approval or an existing partnership.

## Remaining release work

Resolve the [development device identifier](DEVICE-IDENTITY.md), complete the
[release tests](RELEASING.md) on fresh x64 and ARM64 machines, and document an
application-trust distribution route. No paid service has been purchased, no
Windows security setting has been weakened, and no public stable release is
implied by a successful build.
