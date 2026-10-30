using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core.Features;

/// <summary>
/// Sets one bit of the game's own developer cheat flags (ps_actor_flags). Game code tests these bits directly,
/// so no code is patched. The default of every bit is cleared.
/// </summary>
public abstract class ActorFlagFeature(uint mask) : Feature
{
    public override bool NeedsPlayer => false;

    protected override void Apply(GameProcess game, nint player)
    {
        nint address = game.Base + Offsets.PsActorFlags;
        if (game.TryRead<uint>(address, out var flags) && (flags & mask) != mask)
            game.Write(address, flags | mask);
    }

    protected override void Restore(GameProcess game, nint player) => Clear(game);

    protected override void Defaults(GameProcess game, nint player) => Clear(game);

    protected override void OnShutdown(GameProcess game) => Clear(game);

    private void Clear(GameProcess game)
    {
        nint address = game.Base + Offsets.PsActorFlags;
        if (game.TryRead<uint>(address, out var flags) && (flags & mask) != 0)
            game.Write(address, flags & ~mask);
    }
}

/// <summary>
/// Weapons treat ammo as unlimited (weapon_magazined::unlimited_ammo), medkit use does not consume a medkit
/// (medkit::apply_medkit) and throwing does not consume the stack (missile_weapon::throw_me). In addition the
/// reserve ammo stacks are topped up to their slot maximum so the ammo counter reads full.
/// </summary>
public sealed class UnlimitedItems() : ActorFlagFeature(Offsets.ActorFlags.UnlimitedItems)
{
    public override string Name => "Unlimited ammo, medkits & throwables";
    public override bool NeedsPlayer => true;

    protected override void Apply(GameProcess game, nint player)
    {
        base.Apply(game, player);
        FillReserveAmmo(game, player);
    }

    private const uint MaxPlausibleLimit = 999;

    /// <summary>Raises every ammo stack to the player's current carry limit for that ammo type. Crafting resources are left alone.</summary>
    private static void FillReserveAmmo(GameProcess game, nint player)
    {
        nint inventory = game.ReadPointer(player + Offsets.Player.Inventory);
        nint array = inventory == 0 ? 0 : game.ReadPointer(inventory + Offsets.Inventory.AllItems);
        nint level = game.ReadPointer(game.Base + Offsets.GLevel);
        if (array == 0 || level == 0 ||
            !game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count) ||
            !game.TryRead<uint>(player + Offsets.Player.LimitTier, out var tier) || tier > 255)
            return;

        nint table = game.ReadPointer(level + Offsets.Level.LimitTable);
        nint limits = table == 0 ? 0 : game.ReadPointer(table + (nint)tier * Offsets.Level.LimitEntryStride + Offsets.Level.LimitByAmmoKey);
        if (limits == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            nint item = game.ReadPointer(array + i * 8);
            if (item == 0)
                continue;

            nint ammo = item - Offsets.Inventory.ItemOffsetInEntity;
            if (!AmmoItemVtables.Rvas.Contains(game.ReadPointer(ammo) - game.Base))
                continue;

            nint parameters = game.ReadPointer(ammo + Offsets.Inventory.EntityStaticParams);
            if (parameters == 0 ||
                !game.TryRead<byte>(parameters - Offsets.Inventory.CraftResBias, out var craftResource) || craftResource != 0 ||
                !game.TryRead<uint>(parameters - Offsets.Inventory.AmmoKeyBias, out var key) || key >= 64 ||
                !game.TryRead<uint>(limits + (nint)key * 4, out var max) || max is 0 or > MaxPlausibleLimit ||
                !game.TryRead<ushort>(ammo + Offsets.Inventory.AmmoBoxValue, out var units))
                continue;

            // Only ever raises the stack, so a wrong limit cannot take ammo away.
            if (units < max)
                game.Write(ammo + Offsets.Inventory.AmmoBoxValue, (ushort)max);
        }
    }
}

/// <summary>Skips the gas mask filter spending and suffocation check in gasmask::update_state.</summary>
public sealed class FreeGasMask() : ActorFlagFeature(Offsets.ActorFlags.FreeGasMask)
{
    public override string Name => "No filter drain / suffocation";
}
