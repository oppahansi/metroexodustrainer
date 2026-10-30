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

    private void Clear(GameProcess game)
    {
        nint address = game.Base + Offsets.PsActorFlags;
        if (game.TryRead<uint>(address, out var flags) && (flags & mask) != 0)
            game.Write(address, flags & ~mask);
    }
}

/// <summary>
/// Weapons treat ammo as unlimited (weapon_magazined::unlimited_ammo), medkit use does not consume a medkit
/// (medkit::apply_medkit) and throwing does not consume the stack (missile_weapon::throw_me).
/// </summary>
public sealed class UnlimitedItems() : ActorFlagFeature(Offsets.ActorFlags.UnlimitedItems)
{
    public override string Name => "Unlimited ammo, medkits & throwables";
}

/// <summary>Skips the gas mask filter spending and suffocation check in gasmask::update_state.</summary>
public sealed class FreeGasMask() : ActorFlagFeature(Offsets.ActorFlags.FreeGasMask)
{
    public override string Name => "No filter drain / suffocation";
}
