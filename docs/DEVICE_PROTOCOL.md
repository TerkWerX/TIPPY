# Infinity / AltoEdge Version 14 HID notes

Verified on Windows 11 with both pedals attached simultaneously:

| Product string | Vendor ID | Product ID | Windows input report length |
|---|---:|---:|---:|
| VEC USB Footpedal | `05F3` | `00FF` | 3 bytes |
| AltoEdge USB Footpedal | `05F3` | `00FF` | 3 bytes |

Both units expose the same HID report descriptor:

```text
05 0C 09 03 A1 01 05 09 19 01 29 03 15 00 25 01
35 00 45 01 65 00 55 00 75 01 95 03 81 02 95 0D
81 03 C1 00
```

The descriptor declares three one-bit programmable buttons followed by 13 padding bits. HidSharp includes a leading report-ID byte even though the descriptor does not declare numbered report IDs, producing this Windows representation:

```text
byte 0: report ID (0)
byte 1: button bit mask (bit 0 left, bit 1 center, bit 2 right)
byte 2: padding (0)
```

The decoder also accepts the older eight-byte event representation reported by some Linux HID paths:

```text
switch index, 00, 00, 09, pressed, 00, 00, 00
```

Each physical HID path is hashed into a separate profile key when the device has no serial number. This lets identical pedals operate concurrently and retain separate mappings per USB topology.
