using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core.Features;

/// <summary>
/// Keeps every firearm's loaded-ammo counter at the highest value it has shown, so shooting never empties the
/// magazine. Only objects whose vtable belongs to a weapon_magazined-derived class are written to, and the
/// counter is only raised when it dropped, so reloads (which raise it) and the unlimited-reserve feature still
/// work as usual.
/// </summary>
public sealed class InfiniteMagazine : Feature
{
    private const uint SaneMax = 1000;

    // Highest loaded-ammo value seen per weapon object; objects are keyed by address.
    private readonly Dictionary<nint, uint> _max = [];

    public override string Name => "Infinite magazine (no reload needed)";

    protected override void OnEnable(GameProcess game, nint player) => _max.Clear();

    protected override void Apply(GameProcess game, nint player)
    {
        nint inventory = game.ReadPointer(player + Offsets.Player.Inventory);
        if (inventory == 0)
            return;

        nint array = game.ReadPointer(inventory + Offsets.Inventory.AllItems);
        if (array == 0 || !game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count))
            return;

        var seen = new HashSet<nint>();
        for (int i = 0; i < count; i++)
        {
            nint item = game.ReadPointer(array + i * 8);
            if (item == 0)
                continue;

            nint weapon = item - Offsets.Inventory.ItemOffsetInEntity;
            long vtableRva = game.ReadPointer(weapon) - game.Base;
            if (!MagazinedWeaponVtables.Rvas.Contains(vtableRva))
                continue;

            nint address = weapon + Offsets.Inventory.WeaponAmmoLoaded;
            if (!game.TryRead<uint>(address, out var loaded) || loaded > SaneMax)
                continue;

            seen.Add(weapon);
            _max.TryGetValue(weapon, out var max);
            if (loaded > max)
                _max[weapon] = loaded;
            else if (loaded < max)
                game.Write(address, max);
        }

        // Forget weapons that left the inventory so a reused address starts fresh.
        foreach (var key in _max.Keys.Where(k => !seen.Contains(k)).ToList())
            _max.Remove(key);
    }

    // Nothing to give back: the counter simply stops being refilled.
    protected override void Restore(GameProcess game, nint player) => _max.Clear();
}
