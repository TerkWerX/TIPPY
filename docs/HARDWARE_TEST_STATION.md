# Hardware certification and HIL testing

Tippy has two related real-device workflows. Neither can create a passing report from simulated input.

## Hardware Passport

Hardware Passport automatically evaluates every connected pedal against these checks:

- at least two complete press/release cycles per switch;
- real simultaneous state for multi-switch hardware;
- disconnect and reconnect;
- unplug while a switch is held, followed by Tippy's synthetic release;
- no switches left down;
- internal routing latency median at or below 1 ms and p99 at or below 5 ms.

The exported `.tippy-passport.json` includes a certificate ID, device and descriptor fingerprints, explicit requirements, percentile results, and privacy-safe raw report samples. A functional device whose test machine misses the performance target is labeled `functional-pass-performance-review`, not silently rejected or promoted.

## Hardware-in-the-loop station

The HIL station is the longer physical regression workflow. Assign at least one real output to the test pedal, then complete ten cycles per switch, simultaneous input, reconnect, and unplug-while-held. Tippy correlates the timestamp from the actual asynchronous HID read with the timestamp immediately after the macro output is dispatched.

The station requires at least five mapped-output samples (or two per switch, whichever is higher), output p50 at or below 5 ms, and p99 at or below 10 ms. It exports `.tippy-hil.json` evidence suitable for a device-support review. This measures the Tippy/Windows dispatch path; an external high-speed camera or electrical loopback can still be used when total mechanical-to-application latency is required.

These physical runs are intentionally separate from ordinary CI. Pure state machines, percentile grading, analog ownership, OSC packet creation, and pack authentication are unit-tested on every build; actual hardware claims must come from an exported Passport and HIL report captured with that device connected.
