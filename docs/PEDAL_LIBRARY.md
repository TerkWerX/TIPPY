# Pedal artwork and identity library

Tippy's pedal presentation is data-driven. The bundled `PedalLibrary` directory contains `pedal_registry.json` plus transparent, straightened PNG artwork. Images are rendered with `Uniform` scaling so their aspect ratio is never stretched.

At startup and after USB hot-plug scans, Tippy reloads the registry. It matches exact VID/PID first and then uses manufacturer and product strings for families whose public PID information is incomplete. A registry entry with no image receives a labeled generic switch layout.

`VID_05F3/PID_00FF` is shared by numerous VEC-built and rebadged controls. Tippy therefore starts with the generic shared-shell picture, offers the user a model picker, and saves the selected artwork on that device profile. The **Picture** command on every pedal card can override any automatic match.

Unmatched likely pedals and registry entries marked unverified are appended to `unknown_pedals.log` with timestamp, VID, PID, manufacturer, and product strings. Development copies place this beside the source artwork library; installed copies fall back to `%LOCALAPPDATA%\Tippy`. This log contains device descriptors only—never keystrokes or macro contents.

Developers can point a build at a replacement library with the `TIPPY_PEDAL_LIBRARY` environment variable. Keep the registry and PNG files together. Artwork candidates placed in the directory are included in the manual picker even before a registry entry references them.
