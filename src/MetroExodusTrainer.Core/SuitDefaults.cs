namespace MetroExodusTrainer.Core;

/// <summary>Reads the player's real default speed and jump from the game's per-suit parameter table.</summary>
public static class SuitDefaults
{
    public static bool TryRead(GameProcess game, nint player, out float speed, out float jump)
    {
        speed = jump = 0f;
        nint level = game.ReadPointer(game.Base + Offsets.GLevel);
        if (level == 0 ||
            !game.TryRead<uint>(player + Offsets.Player.SuitIndex, out var index) ||
            !game.TryRead<ushort>(level + Offsets.Level.SuitCount, out var count) ||
            index >= count)
            return false;

        nint table = game.ReadPointer(level + Offsets.Level.SuitTable);
        if (table == 0)
            return false;

        nint entry = table + (nint)index * Offsets.Level.SuitStride;
        return game.TryRead(entry + Offsets.Level.SuitSpeed, out speed) &&
               game.TryRead(entry + Offsets.Level.SuitJump, out jump);
    }
}
