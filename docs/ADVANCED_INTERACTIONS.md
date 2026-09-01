# Advanced foot interactions

Tippy treats a switch as more than a single shortcut while keeping physical press and release state authoritative.

## Per-switch gestures

Every macro assignment retains its normal press and independent release actions. It can additionally define:

- **Double tap:** two short presses inside an adjustable 150–900 ms window. The first tap is delayed only when double-tap recognition is configured.
- **Long press:** a separate action after an adjustable 250–3000 ms physical hold. Releasing sooner runs the ordinary tap action.
- **Repeat while held:** runs the press action again after the configured delay and interval, bounded by the profile repeat-duration safety limit.
- **Toggle:** alternates a held keyboard, mouse, or gamepad action on and off. Disconnect, Windows lock/suspend, emergency stop, profile change, or Tippy exit releases it.

Toggle cannot be combined with double tap, long press, or repeat. Repeat cannot be combined with double tap or long press. Tippy validates these conflicts when the assignment is saved.

## Foot combinations and sequences

A combination fires when all configured switches are down inside its time window. A sequence fires when its ordered press history is recognized inside the time window. Patterns may span different physical pedals and can be entered manually or recorded with **Capture with feet**. Pattern actions run in addition to each switch's normal assignment, making them safe to add without silently changing existing mappings.

## Creative outputs

Mouse movement and wheel steps become continuous when the macro uses **Hold keys/buttons until released**. A held mouse button plus a movement step creates a drag action.

MIDI messages use these formats:

```text
note:channel:note:velocity
cc:channel:controller:value
pc:channel:program
```

OSC steps accept a `/path`, comma-separated integer/decimal/text arguments, and a `host:port` destination. Both systems open resources lazily and perform no background scanning.

## Rehearsal and emergency stop

Rehearsal Mode recognizes presses, gestures, banks, application context, and foot patterns while suppressing all action output. The global emergency stop cancels running work and releases every tracked keyboard, mouse, and virtual gamepad hold. Profile safety settings bound macro seconds, repeat seconds, and step count.
