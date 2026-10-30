namespace MetroExodusTrainer.Core.Features;

/// <summary>Keeps health full and sets the engine's invulnerable and immortal flags.</summary>
public sealed class Invulnerability : Feature
{
    private bool _origInvulnerable, _origImmortal;

    public override string Name => "Invulnerability";

    protected override void OnEnable(GameProcess game, nint player)
    {
        _origInvulnerable = game.GetBit(player + Offsets.Player.Invulnerable, Offsets.Player.InvulnerableBit);
        _origImmortal = game.GetBit(player + Offsets.Player.Immortal, Offsets.Player.ImmortalBit);
    }

    protected override void Apply(GameProcess game, nint player)
    {
        game.SetBit(player + Offsets.Player.Invulnerable, Offsets.Player.InvulnerableBit, true);
        game.SetBit(player + Offsets.Player.Immortal, Offsets.Player.ImmortalBit, true);
        // Health is normalized to 0..1; only top it up while alive.
        if (game.TryRead<float>(player + Offsets.Player.Health, out var health) && health > 0f && health < 1f)
            game.Write(player + Offsets.Player.Health, 1f);
    }

    protected override void Restore(GameProcess game, nint player)
    {
        game.SetBit(player + Offsets.Player.Invulnerable, Offsets.Player.InvulnerableBit, _origInvulnerable);
        game.SetBit(player + Offsets.Player.Immortal, Offsets.Player.ImmortalBit, _origImmortal);
    }
}

public sealed class InfiniteStamina : Feature
{
    public override string Name => "Infinite stamina";

    protected override void Apply(GameProcess game, nint player) =>
        game.Write(player + Offsets.Player.Stamina, 1f);
}

/// <summary>Writes a float field every tick and restores the value it found when switched on.</summary>
public abstract class FloatOverride(nint offset, float value) : Feature
{
    private float _original = 1f;

    protected override void OnEnable(GameProcess game, nint player)
    {
        if (game.TryRead<float>(player + offset, out var current))
            _original = current;
    }

    protected override void Apply(GameProcess game, nint player) => game.Write(player + offset, value);

    protected override void Restore(GameProcess game, nint player) => game.Write(player + offset, _original);
}

/// <summary>Movement speed multiplier (1.0 is normal).</summary>
public sealed class FastMovement() : FloatOverride(Offsets.Player.BaseSpeed, 1.75f)
{
    public override string Name => "Fast movement";
}

/// <summary>Jump height multiplier (1.0 is normal).</summary>
public sealed class HighJump() : FloatOverride(Offsets.Player.BaseJump, 2.25f)
{
    public override string Name => "High jump";
}
