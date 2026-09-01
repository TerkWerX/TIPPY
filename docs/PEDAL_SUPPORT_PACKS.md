# Pedal support packs

Tippy accepts data-only `.tippy-pedal-pack.zip` archives. Packs cannot contain executable code. Installation permits JSON, PNG, and CSV only, rejects traversal and duplicate paths, limits extracted content to 80 MB, and verifies every file against its SHA-256 digest before copying it into the user library.

## Trusted catalog and updates

**Tools → Browse & update pedal support packs** loads the HTTPS catalog in `pedal-packs/catalog.json`. Each entry pins the archive SHA-256, download URL, version, and publisher ID. A catalog download is accepted only when its publisher already exists in Tippy's bundled `trusted-publishers.json`. Tippy then performs two independent checks:

1. The downloaded archive must match the catalog SHA-256.
2. The pack manifest must carry a valid RSA-SHA256 signature from that trusted publisher.

Installed versions are recorded separately under `%LOCALAPPDATA%\Tippy\PedalLibrary\installed-packs`, so the catalog can offer updates without a background service. Catalog access happens only when the user opens or refreshes the window.

Local packs can still be installed for development and recovery. Tippy labels an unsigned local pack clearly; catalog delivery always requires publisher authentication.

## Signed manifest

The ZIP root must contain `pack-manifest.json`:

```json
{
  "pack_id": "community-pedals-2026-09",
  "version": "1.0.0",
  "publisher_id": "terkwerx-official-2026",
  "signature_algorithm": "RSA-SHA256",
  "signature": "BASE64_SIGNATURE",
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

The signature payload is UTF-8 text containing `pack_id`, `version`, and `publisher_id` on separate lines followed by every `path:SHA256` pair sorted by path. `tools/Sign-TippySupportPack.ps1` produces the exact canonical signature Tippy verifies.

```powershell
.\tools\Sign-TippySupportPack.ps1 `
  -ManifestPath .\pack\pack-manifest.json `
  -PrivateKeyPath "$env:LOCALAPPDATA\Tippy\PublisherKeys\terkwerx-official-2026-private.pem"
```

Never add a publisher private key to Git. Only the public key belongs in `pedal-packs/trusted-publishers.json`.

The installed library lives under `%LOCALAPPDATA%\Tippy\PedalLibrary` and overrides the bundled registry. A portable copy may also load `TippyData\PedalLibrary`, and developers can use `TIPPY_PEDAL_LIBRARY`. Artwork still uses uniform scaling and may be overridden per physical device.
