namespace MetroExodusTrainer.Core;

/// <summary>
/// A reversible patch of game code. It only applies when the bytes at the target are exactly the
/// expected original ones, so a different game build can never be corrupted by a stale address.
/// </summary>
public sealed class CodePatch(string name, Func<nint> rva, byte[] expected, byte[] replacement)
{
    public string Name { get; } = name;
    public bool Applied { get; private set; }

    /// <summary>Why the last <see cref="TryApply"/> failed, or null.</summary>
    public string? Error { get; private set; }

    public bool TryApply(GameProcess game)
    {
        if (Applied)
            return true;

        var current = game.ReadBytes(game.Base + rva(), expected.Length);
        if (current is null)
            return Fail("could not read game code");
        if (!current.AsSpan().SequenceEqual(expected))
            return Fail("game code differs from the expected build");
        if (!game.WriteCode(game.Base + rva(), replacement))
            return Fail("could not write game code");

        Applied = true;
        Error = null;
        return true;
    }

    public void Revert(GameProcess game)
    {
        if (!Applied)
        {
            // An earlier trainer run that was killed may have left the patch behind; it is recognized by its exact bytes.
            var current = game.ReadBytes(game.Base + rva(), replacement.Length);
            if (current is not null && current.AsSpan().SequenceEqual(replacement))
                game.WriteCode(game.Base + rva(), expected);
            return;
        }
        game.WriteCode(game.Base + rva(), expected);
        Applied = false;
    }

    private bool Fail(string reason)
    {
        Error = $"{Name}: {reason}";
        return false;
    }
}

/// <summary>Removes a detour (<c>jmp [rip+0]</c> plus nop padding) that an earlier, killed trainer run left in the game.</summary>
internal static class Detour
{
    public static void RemoveLeftover(GameProcess game, nint site, byte[] original)
    {
        if (original.Length < 14)
            return;
        var current = game.ReadBytes(site, original.Length);
        if (current is null ||
            !current.AsSpan(0, 6).SequenceEqual(new byte[] { 0xFF, 0x25, 0, 0, 0, 0 }) ||
            current.Skip(14).Any(b => b != 0x90))
            return;
        game.WriteCode(site, original);
    }
}
