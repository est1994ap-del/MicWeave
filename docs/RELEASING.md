# Publishing and release checklist

## Publish the source preview

This repository is intended to be public so others can review/contribute and a
device-identity inquiry can reference the implementation. Public source is not a
claim that the app is ready for general users.

1. Run `scripts/Audit-PublicSource.ps1` against the staged/tracked tree.
2. Review `git diff --cached` and the tracked-file list manually.
3. Run `scripts/Build.ps1` from a fresh exported/checked-out source directory.
4. Create an empty **MicWeave** repository under the actual maintainer's GitHub
   account. Do not invent a username from an email address. Do not expose private
   email in commits; use the account's verified GitHub no-reply commit address.
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
      preservation of user settings/shared USB transport. The current installer
      has not passed this lifecycle test.
- [ ] Independently review the security-sensitive helper/transport integration.
- [ ] Rebuild from clean source, verify every bundled license and dependency hash,
      inspect payload for private paths/reports, and save SHA-256 checksums.
- [ ] Record precise Windows/transport/runtime versions tested and known issues.
- [ ] Decide whether to sign the user-mode installer; unsigned app builds may show
      Windows reputation warnings. This is separate from kernel-driver signing.

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

- Licensed clean source tree prepared; no remote repository or release is implied.
- Go tests/vet, ten mixer tests, and eight Windows audio checks passed locally.
- The clean exported source built successfully on Windows with the pinned SDK;
  Go tests/vet and all ten application self-tests passed. The package scan found
  no personal account path, supplied private email, or known local device IDs.
- Fresh-machine installer lifecycle and public GitHub Actions execution are not
  covered by the development-machine tests.
