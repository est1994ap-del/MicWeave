# Security

This is an early source preview. No independent security audit or certification
has been completed. Read [the review](docs/SECURITY-REVIEW.md) for scope,
implemented defenses, tests, and limitations.

Do not put secrets, raw audio, personal logs, device IDs, or exploit details in a
public issue. Use the repository's **Security > Report a vulnerability** feature
if the owner has enabled private vulnerability reporting. Until then, open only
a non-sensitive request for a private contact route; do not post the vulnerability.
There is not yet a staffed response service or guaranteed response time.

Keep Windows and the shared usbip-win2 transport updated. MicWeave does not
automatically update the transport and cannot assess every installed third-party
driver. Do not open its loopback ports to the network or run the app as administrator.
