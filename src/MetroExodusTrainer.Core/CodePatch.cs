namespace MetroExodusTrainer.Core;

/// <summary>
/// A reversible patch of game code. It only applies when the bytes at the target are exactly the
/// expected original ones, so a different game build can never be corrupted by a stale address.
/// </summary>
public sealed class CodePatch(string name, nint rva, byte[] expected, byte[] replacement)
{
    public string Name { get; } = name;
    public bool Applied { get; private set; }

    /// <summary>Why the last <see cref="TryApply"/> failed, or null.</summary>
    public string? Error { get; private set; }

    public bool TryApply(GameProcess game)
    {
        if (Applied)
            return true;

        var current = game.ReadBytes(game.Base + rva, expected.Length);
        if (current is null)
            return Fail("could not read game code");
        if (!current.AsSpan().SequenceEqual(expected))
            return Fail("game code differs from the expected build");
        if (!game.WriteCode(game.Base + rva, replacement))
            return Fail("could not write game code");

        Applied = true;
        Error = null;
        return true;
    }

    public void Revert(GameProcess game)
    {
        if (!Applied)
            return;
        game.WriteCode(game.Base + rva, expected);
        Applied = false;
    }

    private bool Fail(string reason)
    {
        Error = $"{Name}: {reason}";
        return false;
    }
}
