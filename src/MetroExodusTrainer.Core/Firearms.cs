using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core;

/// <summary>Finds the player's firearms (objects whose class derives from weapon_magazined) in the inventory.</summary>
public static class Firearms
{
    /// <summary>Addresses of weapon entities in the player's inventory.</summary>
    public static List<nint> Find(GameProcess game, nint player)
    {
        var weapons = new List<nint>();
        nint inventory = game.ReadPointer(player + Offsets.Player.Inventory);
        nint array = inventory == 0 ? 0 : game.ReadPointer(inventory + Offsets.Inventory.AllItems);
        if (array == 0 || !game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count))
            return weapons;

        for (int i = 0; i < count; i++)
        {
            nint item = game.ReadPointer(array + i * 8);
            if (item == 0)
                continue;

            nint weapon = item - Offsets.Inventory.ItemOffsetInEntity;
            if (MagazinedWeaponVtables.Rvas.Contains(game.ReadPointer(weapon) - game.Base))
                weapons.Add(weapon);
        }
        return weapons;
    }
}
