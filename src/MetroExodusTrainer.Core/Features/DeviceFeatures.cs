using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core.Features;

/// <summary>Finds inventory items (gas mask, charger, ...) by the vtable of their entity.</summary>
internal static class InventoryItems
{
    public static IEnumerable<nint> Find(GameProcess game, nint player, HashSet<long> vtables)
    {
        nint inventory = game.ReadPointer(player + Offsets.Player.Inventory);
        nint array = inventory == 0 ? 0 : game.ReadPointer(inventory + Offsets.Inventory.AllItems);
        if (array == 0 || !game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count))
            yield break;

        for (int i = 0; i < count; i++)
        {
            nint item = game.ReadPointer(array + i * 8);
            if (item == 0)
                continue;
            nint entity = item - Offsets.Inventory.ItemOffsetInEntity;
            if (vtables.Contains(game.ReadPointer(entity) - game.Base))
                yield return entity;
        }
    }
}

/// <summary>Keeps the gas mask undamaged: its health (durability, 1 = new) is only ever raised back to full.</summary>
public sealed class GasMaskNeverBreaks : Feature
{
    public override string Name => "Gas mask never breaks";

    public override IReadOnlyList<string> Requires => ["Vtables"];

    protected override void Apply(GameProcess game, nint player)
    {
        foreach (nint mask in InventoryItems.Find(game, player, GasmaskVtables.Rvas))
            if (game.TryRead<float>(mask + Offsets.Entities.Health, out var health) && health > 0f && health < 1f)
                game.Write(mask + Offsets.Entities.Health, 1f);
    }
}

/// <summary>
/// Keeps the hand charger's voltage full, so the flashlight and night vision it powers never run down. Only ever raises
/// the voltage.
/// </summary>
public sealed class UnlimitedBattery : Feature
{
    public override string Name => "Unlimited flashlight & night vision charge";

    public override IReadOnlyList<string> Requires => ["Vtables"];

    protected override void Apply(GameProcess game, nint player)
    {
        foreach (nint charger in InventoryItems.Find(game, player, ChargerVtables.Rvas))
        {
            if (game.TryRead<float>(charger + Offsets.Charger.Voltage, out var voltage) && voltage >= 0f && voltage < 1f)
                game.Write(charger + Offsets.Charger.Voltage, 1f);
            if (game.TryRead<float>(charger + Offsets.Charger.TargetVoltage, out var target) && target >= 0f && target < 1f)
                game.Write(charger + Offsets.Charger.TargetVoltage, 1f);
        }
    }
}
