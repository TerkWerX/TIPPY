# Changelog

Notable changes to Tippy are documented here.

## Unreleased

### Added

- Independent press and release actions on every switch, with backward-compatible migration of existing release-trigger assignments.
- Program-launch macro steps with optional arguments and working directory.
- True momentary shift layers: hold one switch to make the other switches use another bank, including safe multi-pedal release handling.
- Event-driven foreground-application profiles with an independent target bank for every pedal and no continuous process polling.
- Registry-driven USB pedal artwork, shared-ID model chooser, persistent per-device picture overrides, generic labeled placeholders, hot-plug registry reloads, and unknown-device audit logging.
- Bundled community pedal image library and its integration handoff documentation.
- Optional start-minimized-to-tray setting.

- Fail-safe held-output tracking with shared-key reference counts, disconnect/lock/suspend cleanup, one-shot macro cleanup, and an emergency **Release all held inputs** tray command.
- Dynamic, idle-armed learning and runtime support for raw-HID pedals with 1–32 digital switches, plus a generic layout for non-Infinity hardware.
- **Run once when released** assignment behavior and automatic closing key-up events for timed recordings stopped mid-keypress.
- Bounded HID reconnect backoff after open or reader failures.
- Compatibility, support-tier, performance-target, and future-device roadmap documentation.
- Configurable tile layout with automatic or 1–6-column grids for larger pedal collections.
- Dedicated compact tabbed mode that shows one full-size pedal at a time in a smaller application window.
- Persistent selected pedal tab and tile-column preference in profile schema version 4.
- Drag reordering through pedal cards and compact tab headers.
- **To tray** command with notification-area restore and exit controls; pedal input remains active while hidden.
- Added OBS Studio, Streamlabs Desktop, vMix, XSplit Broadcaster, and Wirecast paired bindings for scene/camera switching and live-production controls.

## [0.3.0] - 2026-09-01

### Added

- Concurrent Infinity IN-USB-2 and AltoEdge IN-AE-S support.
- User-learned mappings for additional USB HID pedals.
- Three independent banks per pedal with portable bank save/load/copy workflows.
- Realistic, responsive pedal cards with live switch illumination and drag reordering.
- Windows action, key, text, timed macro, mouse, and optional virtual gamepad output.
- Searchable application shortcut browser with 472 shortcuts for 27 Windows programs.
- Light and dark themes, branded splash screen, About window, mascot, and Windows icon suite.
- Automatic profile persistence and portable profile files.

### Fixed

- Prevented the Applications tab shortcut-count binding from terminating the assignment window.
- Improved light-theme and dropdown text contrast.
- Matched the AltoEdge pedal artwork scale and geometry to the Infinity hardware.
