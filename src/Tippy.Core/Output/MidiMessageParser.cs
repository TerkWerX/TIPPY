namespace Tippy.Core.Output;

public enum MidiShortMessageKind
{
    NoteOn,
    NoteOff,
    ControlChange,
    ProgramChange
}

public readonly record struct MidiShortMessage(
    MidiShortMessageKind Kind,
    int Channel,
    int Data1,
    int Data2,
    uint PackedValue)
{
    public bool IsNoteOn => Kind == MidiShortMessageKind.NoteOn;

    public MidiShortMessage ToNoteOff(int releaseVelocity = 0)
    {
        if (!IsNoteOn) throw new InvalidOperationException("Only a MIDI note-on message can be converted to note-off.");
        releaseVelocity = MidiMessageParser.RequireRange(releaseVelocity, 0, 127, "release velocity");
        return MidiMessageParser.Create(MidiShortMessageKind.NoteOff, Channel, Data1, releaseVelocity);
    }
}

public static class MidiMessageParser
{
    public static MidiShortMessage Parse(string? description)
    {
        var parts = (description ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) throw FormatError();

        var kind = parts[0].ToLowerInvariant() switch
        {
            "note" or "noteon" => MidiShortMessageKind.NoteOn,
            "noteoff" or "off" => MidiShortMessageKind.NoteOff,
            "cc" => MidiShortMessageKind.ControlChange,
            "pc" => MidiShortMessageKind.ProgramChange,
            _ => throw FormatError()
        };

        var expectedParts = kind == MidiShortMessageKind.ProgramChange ? 3 : 4;
        if (parts.Length != expectedParts)
        {
            throw new ArgumentException(kind switch
            {
                MidiShortMessageKind.NoteOn => "MIDI note-on requires note:channel:note:velocity.",
                MidiShortMessageKind.NoteOff => "MIDI note-off requires noteoff:channel:note:releaseVelocity.",
                MidiShortMessageKind.ControlChange => "MIDI control change requires cc:channel:controller:value.",
                _ => "MIDI program change requires pc:channel:program."
            });
        }

        var channel = RequireRange(Parse(parts[1], "channel"), 1, 16, "channel");
        return kind switch
        {
            MidiShortMessageKind.NoteOn => Create(kind, channel,
                MidiData(parts[2], "note"), MidiData(parts[3], "velocity")),
            MidiShortMessageKind.NoteOff => Create(kind, channel,
                MidiData(parts[2], "note"), MidiData(parts[3], "release velocity")),
            MidiShortMessageKind.ControlChange => Create(kind, channel,
                MidiData(parts[2], "controller"), MidiData(parts[3], "value")),
            MidiShortMessageKind.ProgramChange => Create(kind, channel,
                MidiData(parts[2], "program"), 0),
            _ => throw FormatError()
        };
    }

    public static MidiShortMessage Create(MidiShortMessageKind kind, int channel, int data1, int data2 = 0)
    {
        channel = RequireRange(channel, 1, 16, "channel");
        data1 = RequireRange(data1, 0, 127, kind switch
        {
            MidiShortMessageKind.ControlChange => "controller",
            MidiShortMessageKind.ProgramChange => "program",
            _ => "note"
        });
        data2 = RequireRange(data2, 0, 127, kind switch
        {
            MidiShortMessageKind.NoteOn => "velocity",
            MidiShortMessageKind.NoteOff => "release velocity",
            MidiShortMessageKind.ControlChange => "value",
            _ => "data"
        });

        var statusBase = kind switch
        {
            MidiShortMessageKind.NoteOff => 0x80,
            MidiShortMessageKind.NoteOn => 0x90,
            MidiShortMessageKind.ControlChange => 0xB0,
            MidiShortMessageKind.ProgramChange => 0xC0,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var packed = (uint)(statusBase | channel - 1 | data1 << 8 |
                            (kind == MidiShortMessageKind.ProgramChange ? 0 : data2 << 16));
        return new MidiShortMessage(kind, channel, data1, data2, packed);
    }

    internal static int RequireRange(int value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(label, value,
                $"MIDI {label} must be between {minimum} and {maximum}.");
        return value;
    }

    private static int MidiData(string value, string label) => RequireRange(Parse(value, label), 0, 127, label);

    private static int Parse(string value, string label) =>
        int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"Invalid MIDI {label}: {value}");

    private static ArgumentException FormatError() => new(
        "MIDI format must be note:channel:note:velocity, noteoff:channel:note:releaseVelocity, cc:channel:controller:value, or pc:channel:program.");
}
