# Device identity review

Review date: 2026-09-24. **Public binary release is blocked on identity resolution.**

## Finding

The prototype advertises USB vendor/product identifiers `FFFF:4D57` and serial
`MICWEAVE-AUDIO-001`. These values were invented for development. They are not
assigned to MicWeave and are not a reserved allocation. An earlier source comment
claiming reservation was incorrect and has been removed.

An identifier is separate from driver signing. The existing signed USB transport
avoids creating and signing a new MicWeave kernel driver. It does not grant a
vendor/product identifier or certify this virtual device.

USB-IF's [developer information](https://www.usb.org/developers) states its
allocation rules and prohibits unauthorized use of assigned or unassigned VIDs.
Publishing source for review does not establish an allocation for shipping
devices or binary packages.

## Potential no-fee route, not an approval

[pid.codes](https://pid.codes/howto/) accepts open-source device projects and
requires a public source repository with a recognized license. Software-only
projects may need extra justification. Acceptance of MicWeave is not guaranteed.

There is also a standards caveat: pid.codes [explains](https://pid.codes/about/)
that it is not endorsed by USB-IF and that USB-IF lists its VID as obsolete/invalid.
An allocation there would coordinate uniqueness within that community, not confer
USB-IF certification or resolve all compliance questions. Do not describe it as
USB-IF-approved. The maintainer must review this distinction before choosing it.

## Next steps

1. Publish this licensed source preview under the actual maintainer's account.
2. Ask whether a capture-only, software-emulated USB Audio Class device is eligible
   for a community allocation. Use [this draft](IDENTITY-REQUEST.md), with the real
   repository URL. No request has been submitted by preparing the draft.
3. Record the response/allocation and the maintainer's distribution decision.
4. Change the Go descriptor IDs and C# `VirtualDeviceContract` together; add tests
   preventing the provisional identity in public release builds.
5. Test device migration, duplicate detection, restart, upgrade, and removal.
6. Only then remove the release-installer block and publish binaries.

Do not borrow another vendor's ID, claim a pending PID is allocated, or disable
Secure Boot. The current source permits local engineering tests only; it does not
represent a cleared production release.
