# HANDOFF: Integrate pedal image library + device-detection picture swapping into TIPPY

You are the AI programming the TIPPY application. In addition to everything you are already working on, integrate the following new capability. All resources referenced below have already been produced and are on disk — do not regenerate them.

## Where everything lives

**All resources are in `F:\TIPPY\Claude\`** on this machine:

- `F:\TIPPY\Claude\pedals\` — the image library and its data files. Contains:
  - Processed pedal images (`*.png`) — transparent-background PNGs on a uniform 1370×880 canvas, straight top-down view, USB cables edited out, pedal centered and scaled to fill. Named `brand_model.png` (e.g. `olympus_rs-31h.png`).
  - `pedal_registry.json` — **machine-readable device registry. This is your primary integration input.** Maps each device to its image file, brand, model number(s), pedal count, USB VID/PID (hex) with a confidence note, and handling notes.
  - `manifest.csv` — human-readable companion with the same data plus source URLs and rejection history. Treat the JSON as authoritative for code; the CSV is reference.
- `F:\TIPPY\Claude\rejected_angled\` — quarantined photos that failed the quality rule (3/4 angle views). Do NOT use these in the app. They exist only as reference until proper replacements are found.

## What to build (in addition to your current work)

1. **Load the registry at startup** from `pedals\pedal_registry.json`. Do not hardcode the device list — more images and registry entries will be added to this folder over time under the same naming scheme, and the app should pick them up by re-reading the registry.

2. **USB hotplug detection.** Watch for USB HID device arrival/removal (on Windows: WM_DEVICECHANGE or an HID library's hotplug facility). When a device is plugged in, read its VID, PID, manufacturer string, and product string.

3. **Match and swap the picture.** Match the detected device against the registry (`vid`/`pid` first, product string as a secondary hint where the registry says so) and immediately display that device's image wherever TIPPY shows the active pedal. When the device is unplugged, revert appropriately.

4. **Render images correctly.** All images share the same canvas and aspect. Display them as-is: never stretch or alter aspect ratio; letterbox/contain them in whatever UI slot they occupy. Backgrounds are transparent, so they composite on any UI color.

5. **Handle the one deliberately ambiguous entry.** `05F3:00FF` ("VEC Footpedal") is shared by the ENTIRE Infinity/VEC IN-USB family and its rebadges (Infinity IN-USB-1/2/3, Sony FS-85USB, ECS variants, AltoEdge, Start-Stop/WAVpedal bundles, VEC-built X-keys units). VID/PID alone cannot tell them apart. On detecting it: default to the registry's unbranded shell image (`generic-shell_in-usb-1_fs-85usb.png`), then offer the user a picker of the IN-USB-family looks so they can choose the exact pedal they own; persist that choice and use it for future detections. (The library owner is separately generating additional rebadge-look variants via logo swapping; when those PNGs appear in `pedals\`, include them in the picker.)

6. **Entries with `"image": null` are known devices awaiting photos** (e.g. Olympus RS-28H = 07B4:0218, the PCsensor/IKKEGOL cheapies = 0C45:7403/7404). When one is detected, show a generic pedal placeholder labeled with the model name from the registry. When its planned image file (named in the registry notes) appears in `pedals\`, it should start being used without code changes.

7. **Unknown-device logging (important).** For any USB HID foot-switch-like device that matches nothing in the registry — and also whenever a registry entry says its PID is unverified — append VID, PID, manufacturer string, product string, and timestamp to `F:\TIPPY\Claude\unknown_pedals.log`. Several real-world pedal PIDs (Philips ACC/LFH, Olympus RS-27/RS-31, Grundig 540, Kinesis, vPedal) are not in any public USB database, so TIPPY's own logging is how the registry gets completed. Also give the user a manual override: assign any registry image to any detected device, persisted.

8. **Scope rule inherited from the library owner:** USB foot switches only, any button count. If a device is not USB, it is not part of this system.

## Quick registry summary (authoritative data is in pedal_registry.json)

| Image in pedals\ | Device(s) | VID:PID |
|---|---|---|
| olympus_rs-31h.png | Olympus RS-31H / RS-31 (4-pedal) | 07B4:? (log PID) |
| olympus_rs-27h.png | Olympus RS-27H / RS-27 | 07B4:? (log PID) |
| olympus_rs-26.png | Olympus RS-26 | 07B4:0202 ✔ |
| philips_acc2330.png | Philips ACC2310/2320/2330 | 0911:? (log PID) |
| philips_lfh2330.png | Philips LFH2210/2310/2320/2330 | 0911:? (log PID) |
| generic-shell_in-usb-1_fs-85usb.png | whole VEC/Infinity IN-USB family + rebadges (ambiguous — see item 5) | 05F3:00FF ✔ |
| vpedal_vp-1.png | vPedal vP-1 | ? (log; product string 'vPedal') |
| elgato_stream-deck-pedal.png | Elgato Stream Deck Pedal 10GBF9901 | 0FD9:0086 (vendor ✔, PID reported) |
| (awaiting photo) | Olympus RS-28H/RS-28 | 07B4:0218 ✔ |
| (awaiting photo) | PCsensor/IKKEGOL FS1-P/FS3-P family | 0C45:7403 / 0C45:7404 ✔ |
| (awaiting photo) | Cleware F4 | 0D50:0040 ✔ |
| (awaiting photo) | X-keys USB Switch Interface (+3.5mm commercial switch) | 05F3:0232/0261/0264 ✔ |
| (awaiting photo) | Grundig Digta 540 USB, Kinesis Savant Elite2 | ? (log) |

✔ = confirmed against the public usb.ids database. "?" = not publicly documented; TIPPY's runtime logging (item 7) fills these in.

Build this so the image library folder is the single source of truth: new PNGs + registry updates dropped into `F:\TIPPY\Claude\pedals\` extend the app with no code changes.
