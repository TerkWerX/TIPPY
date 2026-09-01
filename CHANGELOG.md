# Changelog

Notable changes to Tippy are documented here.

## Unreleased

### Added

- Reusable named OSC endpoint presets, a live OSC packet test screen, preset-linked macro steps, and standards-tested integer/float/string packet encoding.
- Full virtual Xbox analog output for both thumb sticks and triggers, with configurable intensity, one-shot neutral return, overlap-safe held ownership, and a combined digital/analog driver test.
- A named-variable manager with add, duplicate, remove, token display, built-in values, and live macro-expansion previews.
- Automated Hardware Passport grading with explicit functional/performance results, unplug-while-held synthetic-release certification, latency percentiles, and schema-2 certificate exports.
- A physical hardware-in-the-loop station for ten-cycle soak tests, simultaneous input, reconnects, stuck-release cleanup, and HID-receipt-to-output-dispatch latency reports.
- RSA-SHA256 support-pack publisher authentication, an HTTPS catalog browser, archive pinning, installed-version tracking, authenticated update delivery, bundled trust keys, and publisher signing tooling.

- Complete foreground **Application Scenes** with independent three-bank assignment sets per pedal, optional window-title matching, current-bank capture, and backward-compatible migration from bank-only app profiles.
- **Tippy Hardware Passport** certification for repeated switch operation, simultaneous inputs, clean releases, unplug/reconnect behavior, routing latency, privacy-safe report samples, descriptor fingerprints, and portable `.tippy-passport.json` export.
- Portable per-device `.tippy-device.json` export/import for learned raw-HID mappings.
- Three-sample switch learning with whole-byte volatile-data rejection and mandatory simultaneous-switch validation for multi-switch pedals.
- Per-user **Start with Windows** registration, tray-first startup, unclean-exit detection, local crash logging, and pre-input recovery from the newest automatic profile backup.
- Optional manual or startup GitHub release checks with no account, telemetry, or persistent update service.
- A searchable **Advanced Features** center exposing scenes, Hardware Passport, both pedal learners, combinations, Rehearsal Mode, diagnostics, MIDI, backups, support packs, safety, and portable definitions.
- A per-user Inno Setup installer plus tag-driven GitHub release workflow that creates ZIP/installer/checksum artifacts and activates Authenticode signing automatically when repository certificate secrets are supplied.
- A reorganized Edit Assignment workspace with separate **Windows shortcuts**, **Keyboard keys**, **Applications**, and **Custom macro** tabs; purpose-based category navigation for 61 Windows combinations, 125 individual keys, 32 applications, and 557 application commands; global search that automatically searches all categories; and grouped macro-step tools for keyboard/text/timing, mouse/gamepad, and programs/signals.
- Sub-compact ¼ view with exact quarter-scale pedal artwork, a 210×180 pedal-only window, unlabeled live press illumination, dot-based multi-pedal selection, automatic pressed-pedal focus, and a single full-view return control.
- A clearly labeled, profile-persistent Compact view independent of pedal layout, with pedal-only chrome, full-size artwork, multi-pedal tabs, essential bank/edit controls, and Full view/Esc/F11 escape paths.
- Selectable per-profile Windows MIDI outputs, live note-on/note-off device testing, explicit note-off messages, strict MIDI value validation, endpoint reconnect handling, and accurate WinMM error reporting.
- Five-second branded splash presentation with a white Tippy title and `by TerkWerX.com © 2026` credit.
- Persistent main-window monitor, position, normal size, maximized state, pedal layout, tile columns, and selected compact pedal tab.

- Adjustable per-switch double-tap, long-press, repeat-while-held, and toggle behaviors with explicit conflict validation.
- Cross-device pedal combinations and ordered sequences, including live pattern capture from physical foot presses.
- Continuous mouse movement, horizontal/vertical scrolling, held mouse buttons, MIDI note/CC/program output, and OSC messages.
- Built-in and user-defined macro variables for date, time, clipboard, foreground application, profile, device, switch, and bank context.
- Click-through bank/action overlay and Rehearsal Mode for previewing foot workflows without sending output.
- Live diagnostics for raw reports, simultaneous presses, synthetic releases, routing latency, reconnect state, and privacy-safe JSON support reports.
- Configurable macro duration, repeat duration, step-count limits, and a global emergency-stop shortcut.
- Automatic rolling profile backups, one-click rollback, and restart-based portable USB mode.
- Checksum-verified, data-only `.tippy-pedal-pack.zip` installation with traversal, type, size, and SHA-256 validation.
- Device-specific Windows Raw Input learning for pedals that enumerate as keyboards.
- Running-application capture in the foreground application profile editor.

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

### Fixed

- Checkbox captions now follow Tippy's theme-aware text brush and remain readable in dark mode.

- Full-view window dimensions are now remembered independently for auto, stacked, side-by-side, tabbed, and each tiled column arrangement; returning to a layout restores the user's size without moving the window to another monitor.
- Multi-row layouts now use a dense low-resolution presentation that keeps pedal photographs, action names, and Edit Assignment buttons usable while fitting a two-pedal stacked view into a 700-pixel-tall window.
- Layout sizing stays within the current monitor, adapts pedal artwork when necessary, and establishes a content-measured minimum height so the pedal area never shows a scrollbar.
- The full-view command strip now wraps every command from **Open profile** through the theme toggle into a second row when the window is narrowed, keeping all commands visible and returning to one row when space permits.
- Tightened the permanent light-gray header badge so the enlarged mascot and Tippy wordmark read as one mark with the tagline centered beneath both in light and dark themes.
- Built-in Infinity and AltoEdge pedal artwork now decodes eagerly from embedded PNG streams, preventing blank transparent pedal cards in self-contained builds.
- Layout changes now resize inside the window's current monitor and preserve its top-left position whenever physically possible instead of using the primary monitor's work area.
- Restoring from the notification area now preserves a maximized window instead of always forcing normal state.

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
