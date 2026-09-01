using Tippy.Core.Input;

namespace Tippy.Core.Tests;

public sealed class HeldOutputLedgerTests
{
    [Fact]
    public void SharedModifierStaysDownUntilItsFinalOwnerReleases()
    {
        var ledger = new HeldOutputLedger();

        var firstDown = ledger.Acquire("left", ["Ctrl", "A"]);
        var secondDown = ledger.Acquire("right", ["Ctrl", "B"]);
        var firstUp = ledger.ReleaseOwner("left");
        var secondUp = ledger.ReleaseOwner("right");

        Assert.Equal(["Ctrl", "A"], firstDown.KeysDown);
        Assert.Equal(["B"], secondDown.KeysDown);
        Assert.Equal(["A"], firstUp.KeysUp);
        Assert.Equal(["Ctrl", "B"], secondUp.KeysUp);
    }

    [Fact]
    public void NestedAcquisitionsByOneMacroAreReferenceCounted()
    {
        var ledger = new HeldOutputLedger();

        ledger.Acquire("macro", ["Ctrl"]);
        var chordDown = ledger.Acquire("macro", ["Ctrl", "K"]);
        var chordUp = ledger.Release("macro", ["Ctrl", "K"]);
        var macroUp = ledger.ReleaseOwner("macro");

        Assert.Equal(["K"], chordDown.KeysDown);
        Assert.Equal(["K"], chordUp.KeysUp);
        Assert.Equal(["Ctrl"], macroUp.KeysUp);
    }

    [Fact]
    public void ReleaseAllReturnsEveryOutstandingOutputOnce()
    {
        var ledger = new HeldOutputLedger();
        ledger.Acquire("one", ["Shift"], ["A"]);
        ledger.Acquire("two", ["Shift", "F13"], ["A", "B"]);

        var released = ledger.ReleaseAll();
        var repeated = ledger.ReleaseAll();

        Assert.Equal(["Shift", "F13"], released.KeysUp);
        Assert.Equal(["A", "B"], released.GamepadButtonsUp);
        Assert.True(repeated.IsEmpty);
    }

    [Fact]
    public void KeyAliasesShareTheSamePhysicalOutput()
    {
        var ledger = new HeldOutputLedger();

        var first = ledger.Acquire("one", ["Control", "Esc"]);
        var second = ledger.Acquire("two", ["Ctrl", "Escape"]);
        var firstUp = ledger.ReleaseOwner("one");
        var secondUp = ledger.ReleaseOwner("two");

        Assert.Equal(["Ctrl", "Escape"], first.KeysDown);
        Assert.Empty(second.KeysDown);
        Assert.Empty(firstUp.KeysUp);
        Assert.Equal(["Ctrl", "Escape"], secondUp.KeysUp);
    }

    [Fact]
    public void GamepadAliasesShareTheSamePhysicalOutput()
    {
        var ledger = new HeldOutputLedger();

        var first = ledger.Acquire("one", gamepadButtons: ["Left Shoulder"]);
        var second = ledger.Acquire("two", gamepadButtons: ["LB"]);
        var firstUp = ledger.ReleaseOwner("one");
        var secondUp = ledger.ReleaseOwner("two");

        Assert.Equal(["LB"], first.GamepadButtonsDown);
        Assert.Empty(second.GamepadButtonsDown);
        Assert.Empty(firstUp.GamepadButtonsUp);
        Assert.Equal(["LB"], secondUp.GamepadButtonsUp);
    }
}
