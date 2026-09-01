<p align="center">
  <img src="assets/branding/tippy-toes-tippy.png" width="260" alt="Tippy Toes mascot with the Tippy tattoo">
</p>

<h1 align="center">Tippy</h1>

<p align="center">
  <strong>Turn USB transcription pedals into fast, low-latency keyboard, mouse, macro, and gamepad controls.</strong>
</p>

<p align="center">
  <a href="https://github.com/TerkWerX/TIPPY/actions/workflows/windows-ci.yml"><img alt="Windows CI" src="https://github.com/TerkWerX/TIPPY/actions/workflows/windows-ci.yml/badge.svg"></a>
  <a href="https://github.com/TerkWerX/TIPPY/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/TerkWerX/TIPPY?display_name=tag&sort=semver"></a>
  <img alt="Windows 11 x64" src="https://img.shields.io/badge/Windows-11%20x64-0078D4?logo=windows11&logoColor=white">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white">
</p>

Tippy is a Windows 11 utility for people who want useful work and gameplay controls under their feet without dragging along a full transcription suite. It supports multiple pedals at once, gives every connected pedal its own three macro banks, and makes assignments visible at a glance.

> Tippy is under active development. The Infinity IN-USB-2 Version 14 and AltoEdge IN-AE-S Version 14 are the first verified devices; the built-in learning wizard can teach Tippy additional USB HID pedals.

## See Tippy in action

![Tippy dashboard with Infinity and AltoEdge pedals connected](docs/images/tippy-dashboard.png)

Each pedal has independent banks, realistic switch placement, live press illumination, reusable bank files, and drag-and-drop placement that can match the pedals on your floor.

<details>
<summary><strong>Browse application-specific shortcuts</strong></summary>

![Tippy Applications shortcut browser](docs/images/tippy-applications.png)

The searchable catalog currently includes 472 commands across 27 Windows applications, including Microsoft Office, Adobe creative tools, Blender, GIMP, Maya, Ableton Live, Reason, REAPER, Audacity, VS Code, VLC, and more.

</details>

## Highlights

- Use two or more compatible USB pedals simultaneously with separate assignments.
- Keep three independent banks on every pedal, or switch all pedals together.
- Save, load, and copy portable `.tippy-bank.json` banks to any pedal with enough switches—including loading the same bank on several pedals.
- Assign Windows actions, individual keys, key combinations, text strings, timed recordings, delays, mouse actions, and optional Xbox 360 gamepad buttons.
- Choose tap-once or hold-until-release behavior.
- Arrange pedal cards automatically, stacked, or side by side; drag cards to match their physical order.
- See the pressed switch illuminate in real time.
- Switch between dark and light themes.
- Learn unknown USB HID pedals without a driver or code change.
- Hot-plug devices without restarting Tippy.

## Download and run

1. Download `Tippy-v0.3.0-win-x64.zip` from the [latest release](https://github.com/TerkWerX/TIPPY/releases/latest).
2. Extract the entire ZIP to a folder you control.
3. Run `Tippy.exe`.
4. Plug in a supported pedal and choose **Edit assignment** on a switch.

The release is self-contained, so a separate .NET installation is not required. Tippy currently ships unsigned; Windows SmartScreen may ask you to confirm the first launch.

## Supported pedals

| Device | Status | USB identity |
|---|---|---|
| VEC Infinity IN-USB-2 Version 14 | Verified | `VID_05F3`, `PID_00FF` |
| AltoEdge IN-AE-S Version 14 | Verified | `VID_05F3`, `PID_00FF` |
| Other standard USB HID pedals | Learnable | Use **Learn new pedal** |

The two verified pedals use the same three-button HID protocol. Tippy distinguishes simultaneous physical devices so each can keep its own layout, banks, and assignments.

## Assignments and banks

Choose **Edit assignment** to search Windows or application shortcuts, type a string, capture a key combination, or record a timed sequence. Select Bank 1, 2, or 3 inside a pedal card to change only that pedal. The **All pedals** bank buttons and default `Ctrl+Alt+B` shortcut switch every connected pedal together.

Assign **Switch to next bank** to a footswitch when you want completely hands-free bank changes. Use **Save bank**, **Load bank**, or **Copy to…** to reuse a setup on any compatible connected pedal.

Tippy autosaves its live profile here:

```text
%LOCALAPPDATA%\Tippy\default.tippy.json
```

Portable profiles use `.tippy.json`; portable banks use `.tippy-bank.json`.

## Learn another pedal

1. Select **Learn new pedal** beside **Scan USB**.
2. Pick the device from the HID list. Likely pedal devices are marked with a star.
3. Release every switch, then capture each switch when prompted.
4. Save the mapping and begin assigning actions immediately.

The learned definition stores USB identity, a report-descriptor fingerprint, and switch rules. It does not record normal keyboard typing.

## Gamepad output and game rules

Keyboard and mouse output works through the Windows `SendInput` API. Genuine XInput output requires an existing ViGEmBus 1.22 installation; Tippy can detect and test it under **Settings** but does not install a kernel driver.

Some games and anti-cheat systems reject injected input. A conservative gameplay setup maps one pedal press to one ordinary key. Timed or multi-step automation may be prohibited even in casual modes, so the game's rules always take precedence.

## Build from source

Requirements: Windows 11 x64 and the .NET 8 SDK or newer.

```powershell
git clone https://github.com/TerkWerX/TIPPY.git
cd TIPPY
dotnet build Tippy.slnx
dotnet test Tippy.slnx
dotnet run --project src/Tippy.App/Tippy.App.csproj
```

Create the self-contained release:

```powershell
dotnet publish src/Tippy.App/Tippy.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o dist/Tippy-win-x64
```

The device probe lists HID descriptors and live reports:

```powershell
dotnet run --project tools/Tippy.DeviceProbe/Tippy.DeviceProbe.csproj -- 20
```

See [the device protocol notes](docs/DEVICE_PROTOCOL.md) for the verified Version 14 report format. Contributions are welcome—start with [CONTRIBUTING.md](CONTRIBUTING.md).

## Project layout

```text
src/Tippy.App/          WPF interface, HID service, input playback, and catalogs
src/Tippy.Core/         Profiles, banks, macro models, and report decoders
tests/Tippy.Core.Tests/ Decoder and profile tests
tools/Tippy.DeviceProbe HID discovery and protocol diagnostic tool
assets/                 Mascot, branding, pedal art, and Windows icon suite
docs/                   Protocol notes and project screenshots
```

## Acknowledgments

Tippy was inspired by the idea of using purpose-built macro utilities with unconventional input devices, then shaped around transcription pedals those utilities did not recognize. Product and application names are trademarks of their respective owners. Tippy is not affiliated with VEC, AltoEdge, Microsoft, Adobe, Autodesk, Ableton, or the other cataloged software vendors.
