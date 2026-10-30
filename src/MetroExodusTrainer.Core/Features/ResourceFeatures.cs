using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core.Features;

/// <summary>
/// Keeps the crafting resources (materials and chemicals, the stacks the game marks as craft resources) topped up, so
/// crafting and upgrades never run short. Stacks are only ever raised, never lowered. Nothing needs restoring: the
/// player simply keeps what they were given.
/// </summary>
public sealed class InfiniteCraftResources : Feature
{
    private const ushort Amount = 999;

    public override string Name => "Unlimited crafting resources";

    protected override void Apply(GameProcess game, nint player)
    {
        nint inventory = game.ReadPointer(player + Offsets.Player.Inventory);
        nint array = inventory == 0 ? 0 : game.ReadPointer(inventory + Offsets.Inventory.AllItems);
        if (array == 0 || !game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count))
            return;

        for (int i = 0; i < count; i++)
        {
            nint item = game.ReadPointer(array + i * 8);
            if (item == 0)
                continue;

            nint stack = item - Offsets.Inventory.ItemOffsetInEntity;
            if (!AmmoItemVtables.Rvas.Contains(game.ReadPointer(stack) - game.Base))
                continue;

            nint parameters = game.ReadPointer(stack + Offsets.Inventory.EntityStaticParams);
            if (parameters == 0 ||
                !game.TryRead<byte>(parameters - Offsets.Inventory.CraftResBias, out var craftResource) || craftResource != 1 ||
                !game.TryRead<ushort>(stack + Offsets.Inventory.AmmoBoxValue, out var units))
                continue;

            if (units < Amount)
                game.Write(stack + Offsets.Inventory.AmmoBoxValue, Amount);
        }
    }
}
