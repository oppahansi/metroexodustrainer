using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core;

/// <summary>Finds NPC entities (humans and monsters) in the game's entity list.</summary>
public static class Npcs
{
    /// <summary>Addresses of all live entities whose class derives from base_npc (the player is excluded).</summary>
    public static List<nint> Find(GameProcess game)
    {
        var npcs = new List<nint>();
        nint server = game.ReadPointer(game.Base + Offsets.Entities.GEntities);
        if (server == 0)
            return npcs;

        nint array = game.ReadPointer(server + Offsets.Entities.EntityVector);
        if (array == 0 || !game.TryRead<ushort>(server + Offsets.Entities.EntityVector + 10, out var count) || count == 0)
            return npcs;

        var pointers = game.ReadBytes(array, count * 8);
        if (pointers is null)
            return npcs;

        for (int i = 0; i < count; i++)
        {
            nint entity = (nint)BitConverter.ToInt64(pointers, i * 8);
            if (entity != 0 && NpcVtables.Rvas.Contains(game.ReadPointer(entity) - game.Base))
                npcs.Add(entity);
        }
        return npcs;
    }

    /// <summary>True when the NPC is hostile to the player (efriend_type 2 = enemy).</summary>
    public static bool IsEnemy(GameProcess game, nint npc) =>
        game.TryRead<byte>(npc + Offsets.Entities.FriendType, out var type) && type == Offsets.Entities.FriendTypeEnemy;
}
