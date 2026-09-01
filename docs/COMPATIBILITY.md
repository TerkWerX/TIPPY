# Tippy compatibility and reliability direction

Tippy exists to make useful, low-latency controllers out of USB foot controls whose original software is limited, obsolete, or no longer maintained. The application should remain a small event-driven Windows utility: no service, server, account, telemetry, or continuous high-frequency polling is required.

## Design promises

- **A release must never be optional.** Tippy tracks held keyboard and gamepad outputs by owner, shares common modifiers safely across simultaneous pedals, synthesizes releases when a reader disappears, and releases everything on session lock, suspend, shutdown, or through the tray emergency command.
- **Idle work stays near zero.** Each connected raw-HID pedal uses an asynchronous blocking read. Reconnect attempts use bounded exponential delays rather than a tight polling loop.
- **Device support is layered.** Verified devices receive a built-in decoder. Other raw-HID button devices can be taught locally. A learned definition is data stored in the profile, not a new driver or code plug-in.
- **Hardware truth is visible.** A data-driven registry maps identities and product strings to aspect-preserving artwork. Shared identities prompt for the real shell, and unsupported devices use a labeled generic layout rather than pretending to be different hardware.
- **Profiles stay portable.** Pedal banks and full Tippy profiles remain ordinary JSON files that can be backed up and moved without vendor software.

## Current support tiers

### Verified built-in support

- VEC Infinity IN-USB-2 Version 14
- AltoEdge IN-AE-S Version 14

These devices share the same three-switch HID protocol and can be connected concurrently with independent mappings and banks.

### User-learned raw-HID support

The learning wizard can capture **1–32 digital switches** from an otherwise unknown USB HID input device. It takes three press/release samples per switch, rejects bytes that vary between samples, and requires a real simultaneous-switch validation for multi-switch hardware. Snapshot-style button masks support simultaneous presses; indexed event-style reports are also supported. A matching VID/PID and report descriptor identify the learned device while allowing harmless product-name changes. Individual definitions can be exported and imported as data-only `.tippy-device.json` files.

Learning is intended for digital, momentary buttons. Analog axes, pressure, velocity, vendor initialization commands, encrypted protocols, and devices opened exclusively by another program require a purpose-built adapter.

### Keyboard-emulating pedals

Pedals that enumerate as an ordinary keyboard can be learned through Tippy's Windows Raw Input provider. The mapping is tied to the selected physical Raw Input device path, so the same key on the user's normal keyboard is not remapped. Tippy does not install a global keyboard hook or filter driver. The learner intentionally requires the user to select a raw keyboard device and capture every switch because public USB descriptors often cannot identify these pedals reliably.

## Trigger behavior

- **Run once when pressed** for scene changes, application commands, text, mouse clicks, and timed sequences.
- **Hold keys/buttons until released** for push-to-talk, game movement, momentary playback, and similar controls.
- **Independent release action** for workflows where lifting the foot should perform a second command. Older release-only bindings migrate automatically.
- **Momentary shift layer** to use another bank only while its owner switch remains physically held.

Timed recordings are closed with matching key-up steps if recording stops while a key is still held. One-shot playback also performs a final release pass after completion, cancellation, or failure.

## Hardware and Windows limits

- Digital pedals cannot provide analog travel, pressure, velocity, or positional control that their reports do not contain.
- Pedals without onboard memory still require Tippy to be running on each computer.
- Tippy currently uses Windows `SendInput`; it does not turn the physical pedal into a firmware-level USB keyboard. Elevated windows, exclusive-input games, and anti-cheat systems can reject injected input.
- A pedal unplugged while held is treated as released so it cannot strand a key or virtual gamepad button.

## Completed compatibility infrastructure

- Individual learned-device export/import, descriptor fingerprints, repeated sampling, volatile-byte rejection, and simultaneous-input validation.
- Automated Hardware Passport sessions and privacy-safe certification exports covering repeated switches, simultaneous input, reconnect, unplug-while-held cleanup, release state, and latency percentiles.
- A hardware-in-the-loop station for physical hot-plug, soak, cleanup, and HID-receipt-to-output-dispatch regression reports.
- Authenticated RSA-SHA256 pedal-pack publishers, a browsable HTTPS catalog, archive pinning, installed-version tracking, and user-driven updates.
- A signable release pipeline and installer; repository certificate secrets activate Authenticode signing without code changes.

## Next compatibility milestones

1. Grow the reviewed community registry from exported Hardware Passports and verified device definitions.
2. Add purpose-built adapters for popular Olympus, Philips, vPedal, and other USB transcription controls as hardware reports are verified.
3. Feed reviewed Passport and HIL exports into a community compatibility matrix and optional dedicated CI hardware lab.
4. Add external electrical or optical loopback fixtures where end-to-end mechanical latency certification is required.

## Performance targets

- Event-driven idle CPU usage near zero.
- No per-device polling timer in the normal input path.
- Press-to-output median below 1 ms and 99th percentile below 5 ms on supported hardware.
- Held-output cleanup immediately when a reader ends, with reconnect attempts backing off to at most once every 30 seconds.

Device support claims should be promoted to “verified” only after press, release, simultaneous-switch, unplug-while-held, reconnect, and repeated-use tests on the actual hardware.
