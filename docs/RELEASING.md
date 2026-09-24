# Publishing and release checklist

## Publish the source preview

This repository is intended to be public so others can review/contribute and a
device-identity inquiry can reference the implementation. Public source is not a
claim that the app is ready for general users.

1. Run `scripts/Audit-PublicSource.ps1` against the staged/tracked tree.
2. Review `git diff --cached` and the tracked-file list manually.
3. Run `scripts/Build.ps1` from a fresh exported/checked-out source directory.
4. Use the maintainer's verified **MicWeave** repository. Do not invent a username
   from an email address. Use the maintainer's approved commit identity; do not
   disclose a private address without permission.
5. Commit the reviewed tree and add that exact GitHub URL as `origin`.
6. Push `main`. Check the repository contents and its Actions result online.
7. Enable private vulnerability reporting if desired. Do not claim it is enabled
   until verified in the repository settings.

No executable, driver installer, private diagnostics, original workspace backup,
or unsigned custom-driver experiment belongs in the source upload. Do not push
the parent development folder. Do not publish the old development installer.

The workflow performs read-only validation and never publishes a binary release.
Pinned action revisions and dependency versions need deliberate maintenance.

## Before publishing an installer

All items below are **pending unless explicitly recorded as completed**.

- [ ] Resolve the provisional USB identity, record allocation/maintainer decision,
      and update Go/C# identifiers and migration tests together.
- [ ] Install on a clean supported Windows 11 x64 PC with Secure Boot enabled.
- [ ] Install and run the native ARM64 payload on a clean Windows 11 ARM PC; a
      successful cross-build is not a runtime or driver-installation test.
- [ ] Test Memory Integrity and Smart App Control enabled, plus clear failure
      messages when administrator policy blocks installation. Do not weaken them.
- [ ] Verify transport signature, consent prompt, restart-required handling, and
      recovery from canceled/failed setup without changing other default devices.
- [ ] Verify one microphone/no extra playback endpoints, normal speakers retained.
- [ ] Confirm microphone plus Spotify/browser/second-source audio in at least two
      independently chosen receiving apps (for example, Discord and Zoom).
- [ ] Test different microphones, USB reconnect, sleep/wake, playback device change,
      exclusive-mode conflicts, and multi-user sessions.
- [ ] Test rapid route/unroute, multiple sources, clipping protection, extended
      playback, helper crash/restart, and malicious/slow local clients.
- [ ] Check Windows high-DPI layouts, resize/z-order, keyboard use, source menus,
      independent Active switches, and desktop shortcut/icon.
- [ ] Test upgrade while app is running, downgrade refusal/policy, uninstall, and
      preservation of user settings/shared USB transport. Isolated application-only
      tests are provided in scripts/Test-Installer.ps1; full driver lifecycle and
      native ARM64 testing are still required.
- [ ] Independently review the security-sensitive helper/transport integration.
- [ ] Rebuild from clean source, verify every bundled license and dependency hash,
      inspect payload for private paths/reports, and save SHA-256 checksums.
- [ ] Record precise Windows/transport/runtime versions tested and known issues.
- [ ] Decide whether to sign the user-mode installer; unsigned app builds may show
      Windows reputation warnings **or be blocked by Smart App Control**. This is
      separate from kernel-driver signing. Evaluate a no-fee OSS signing service;
      acceptance is not assumed. See INSTALLATION.md.

When these gates pass, remove the explicit public-installer block in a reviewed
change. Build and tag the exact tested commit, draft release notes, attach only
the verified installer plus checksums/notices, then publish. Never label a preview
as a stable release just because it builds successfully.

## Uninstall behavior

MicWeave's uninstaller removes its application and shortcuts. It preserves
preferences and the shared USBip transport because other software may use it.
If no other software needs that transport, a user may separately remove USBip
through Windows Installed Apps; save work because USB devices may reconnect.

## Local preparation record

- Source preview is published at https://github.com/est1994ap-del/MicWeave.
- The published source-preview GitHub build passed; no public binary release.
- Go tests/vet, ten mixer tests, and eight Windows audio checks passed locally.
- The clean exported source built successfully on Windows with the pinned SDK;
  Go tests/vet and all ten application self-tests passed. The package scan found
  no personal account path, supplied private email, or known local device IDs.
- New setup policy covers native x64/ARM64 selection and cancellation/restart
  outcomes. Both app/helper architectures build; twelve x64 self-tests pass.
- The installer now blocks running-app changes and downgrades, generates its
  uninstall file list from the exact payload (including screenshots), and keeps
  registration after an in-use-file failure so removal can be retried.
- Fresh-machine transport lifecycle, ARM64 runtime, and security-policy coverage
  are not established by these development-machine checks.
