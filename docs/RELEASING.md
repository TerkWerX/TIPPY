# Releasing Tippy

Push a semantic-version tag such as `v0.5.0` to run the Windows release workflow. It tests the solution, publishes a self-contained x64 build, creates both a portable ZIP and a per-user Inno Setup installer, writes SHA-256 checksums, and publishes the artifacts to GitHub Releases.

## Optional Authenticode signing

Add these GitHub Actions secrets to sign every executable, DLL, and installer:

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`: the Base64 contents of a code-signing PFX.
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: the PFX password.

When those secrets are absent, the workflow still creates a clearly unsigned release. Supplying the certificate activates SHA-256 Authenticode signing with a trusted timestamp; no workflow edit is required.

The application update check only reads the public `TerkWerX/TIPPY` latest-release endpoint when the user checks manually or enables startup checks. It sends no profile, device, or usage data.
