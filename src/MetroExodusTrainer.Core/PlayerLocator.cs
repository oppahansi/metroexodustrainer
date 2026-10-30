namespace MetroExodusTrainer.Core;

public static class PlayerLocator
{
    /// <summary>
    /// Returns the address of the player entity, or 0 if no level is loaded or the controlled
    /// entity is not the player (cutscene, vehicle, main menu).
    /// </summary>
    public static nint Find(GameProcess game)
    {
        nint level = game.ReadPointer(game.Base + Offsets.GLevel);
        if (level == 0)
            return 0;

        nint entity = game.ReadPointer(level + Offsets.Level.ControlEntity);
        if (entity == 0)
            return 0;

        nint vtable = game.ReadPointer(entity);
        return vtable - game.Base == Offsets.PlayerVtable ? entity : 0;
    }
}
