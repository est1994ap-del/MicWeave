# Draft community device-identity inquiry

Not sent. Replace the repository placeholder with the public source URL before
submitting. Do not include private email addresses or development-machine logs.

> We maintain MicWeave, an open-source Windows microphone mixer. Its user-mode
> helper emulates a capture-only USB Audio Class 1 device, connected locally using
> the existing signed usbip-win2 transport. Windows' built-in USB audio driver
> exposes one microphone. There is no physical PCB or proprietary firmware.
>
> Source: [INSERT PUBLIC MICWEAVE REPOSITORY URL]
>
> Original code is MIT; upstream-derived device code remains BSD-2-Clause with
> attribution. The entire USB descriptor implementation and transport helper
> source are available in the repository.
>
> A stable device identity is needed so users receive one recognizable microphone,
> avoid collisions with other software devices, and can migrate upgrades reliably.
> This is one shared application/device model, not arbitrary devices created by
> end users. Is this software-only device eligible for your PID allocation process?
> We understand a community allocation is not USB-IF certification.

If eligible, follow the current upstream procedure and select an available,
permitted PID at submission time. Do not assume any numeric value is reserved.
