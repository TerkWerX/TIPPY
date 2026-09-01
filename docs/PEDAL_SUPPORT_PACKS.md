# Pedal support packs

Tippy accepts data-only `.tippy-pedal-pack.zip` archives. Packs cannot contain executable code. The installer permits JSON, PNG, and CSV files only; rejects traversal paths; limits extracted content to 80 MB; and verifies every file against its manifest SHA-256 digest before copying anything into the user pedal library.

The ZIP root must contain `pack-manifest.json`:

```json
{
  "pack_id": "community-pedals-2026-09",
  "version": "1.0.0",
  "files": [
    {
      "path": "pedal_registry.json",
      "sha256": "0123456789ABCDEF..."
    },
    {
      "path": "example_pedal.png",
      "sha256": "FEDCBA9876543210..."
    }
  ]
}
```

The installed library lives under `%LOCALAPPDATA%\Tippy\PedalLibrary` and overrides the bundled registry. A portable copy may also load `TippyData\PedalLibrary`, and developers can use `TIPPY_PEDAL_LIBRARY`. Artwork is still rendered with uniform scaling and may be overridden per physical device.

SHA-256 verification protects pack integrity; it does not establish the publisher's identity. Tippy deliberately labels the current workflow checksum-verified. A future reviewed publishing service can add an authenticated signature without changing the data-only pack structure.
