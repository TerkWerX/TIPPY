using Tippy.Core.Output;

namespace Tippy.Core.Tests;

public sealed class MidiMessageParserTests
{
    [Theory]
    [InlineData("note:1:60:127", MidiShortMessageKind.NoteOn, 1, 60, 127, 0x007F3C90u)]
    [InlineData("noteon:16:64:1", MidiShortMessageKind.NoteOn, 16, 64, 1, 0x0001409Fu)]
    [InlineData("noteoff:16:64:0", MidiShortMessageKind.NoteOff, 16, 64, 0, 0x0000408Fu)]
    [InlineData("off:2:36:45", MidiShortMessageKind.NoteOff, 2, 36, 45, 0x002D2481u)]
    [InlineData("cc:2:7:100", MidiShortMessageKind.ControlChange, 2, 7, 100, 0x006407B1u)]
    [InlineData("pc:10:42", MidiShortMessageKind.ProgramChange, 10, 42, 0, 0x00002AC9u)]
    public void ParsesAndPacksShortMessages(string text, MidiShortMessageKind kind, int channel,
        int data1, int data2, uint packed)
    {
        var message = MidiMessageParser.Parse(text);

        Assert.Equal(kind, message.Kind);
        Assert.Equal(channel, message.Channel);
        Assert.Equal(data1, message.Data1);
        Assert.Equal(data2, message.Data2);
        Assert.Equal(packed, message.PackedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("note:0:60:100")]
    [InlineData("note:17:60:100")]
    [InlineData("note:1:128:100")]
    [InlineData("cc:1:7:128")]
    [InlineData("pc:1:128")]
    [InlineData("note:1:60")]
    [InlineData("unknown:1:2:3")]
    public void RejectsMalformedOrOutOfRangeMessages(string text)
    {
        Assert.ThrowsAny<ArgumentException>(() => MidiMessageParser.Parse(text));
    }

    [Fact]
    public void NoteOnCanCreateMatchingNoteOff()
    {
        var noteOff = MidiMessageParser.Parse("note:3:72:110").ToNoteOff(12);

        Assert.Equal(MidiShortMessageKind.NoteOff, noteOff.Kind);
        Assert.Equal(3, noteOff.Channel);
        Assert.Equal(72, noteOff.Data1);
        Assert.Equal(12, noteOff.Data2);
        Assert.Equal(0x000C4882u, noteOff.PackedValue);
    }
}
