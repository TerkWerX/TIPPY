# Contributing to Tippy

Thanks for helping Tippy support more pedals and workflows.

## Before opening a change

- Search existing issues to avoid duplicating a known problem or request.
- Keep changes focused and explain the user-facing reason for them.
- Do not commit profiles, recorded macros, build output, or device data containing personal identifiers.

## Development setup

Tippy requires Windows 11 x64 and the .NET 8 SDK or newer.

```powershell
dotnet restore Tippy.slnx
dotnet build Tippy.slnx -c Release
dotnet test Tippy.slnx -c Release --no-build
```

## Adding a pedal model

1. Capture descriptors and reports with `tools/Tippy.DeviceProbe`.
2. Add or extend a decoder under `src/Tippy.Core/Input`.
3. Register the decoder in `PedalHidService`.
4. Add anonymized report fixtures and tests under `tests/Tippy.Core.Tests`.
5. Update `docs/DEVICE_PROTOCOL.md` with the verified behavior.

Never include unrelated keyboard input, serial numbers, or machine-specific HID paths in a test fixture.

## Pull requests

Before submitting:

- Build in Release configuration with zero warnings.
- Run the complete test suite.
- Exercise affected interface paths in both light and dark mode.
- Include a screenshot for visible interface changes.
- Describe any new permissions, drivers, or system-level dependencies.
