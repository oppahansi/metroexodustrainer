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

/// <summary>
/// Stops stamina drain and exhaustion. The freeze flag makes update_stamina take its regeneration path,
/// stamina is topped up every tick to cover jump costs, and the "can use stamina" flag (which use_stamina
/// clears when exhausted, blocking fast running and jumping until a timer fires) is kept set.
/// The exhaustion script also revokes sprint through the "movement allowed" bytes, which makes the
/// sprint speed multiplier 0 even with full stamina, so that byte is kept set too.
/// Finally, sprinting pushes a speed modifier (target 1.5) that the game flips back to 1.0 after ~13 s,
/// which is the slowdown players call exhaustion; while sprint is held the boost target is put back.
/// </summary>
public sealed class InfiniteStamina : Feature
{
    private const float Epsilon = 0.001f;

    private bool _origFreeze;
    private float _boost = 1f;       // highest sprint-boost target seen while sprinting
    private float _lastTarget = 1f;  // top modifier target on the previous tick
    private bool _forced;            // we overrode a target to _boost and must give it back

    public override string Name => "Infinite stamina";

    protected override void OnEnable(GameProcess game, nint player)
    {
        _origFreeze = game.Read<byte>(player + Offsets.Player.StaminaFreeze) != 0;
        _boost = _lastTarget = 1f;
        _forced = false;
    }

    protected override void Apply(GameProcess game, nint player)
    {
        game.Write<byte>(player + Offsets.Player.StaminaFreeze, 1);
        game.Write(player + Offsets.Player.Stamina, 1f);
        game.Write<byte>(player + Offsets.Player.CanUseStamina, 1);
        game.Write<byte>(player + Offsets.Player.VsMoveAllowed + Offsets.Player.VsMoveAllowedSprint, 1);
        KeepSprintBoost(game, player);
    }

    private void KeepSprintBoost(GameProcess game, nint player)
    {
        bool sprinting = game.TryRead<uint>(player + Offsets.Player.StateReal, out var state) &&
                         (state & Offsets.Player.SprintStateFlag) != 0;
        nint target = TopModifierTarget(game, player);
        if (target == 0)
        {
            _lastTarget = 1f;
            _forced = false;
            return;
        }

        if (!game.TryRead<float>(target, out var value))
            return;

        if (!sprinting)
        {
            // Sprint released: hand control back to the game if we were holding the boost.
            if (_forced)
                game.Write(target, 1f);
            _forced = false;
            _lastTarget = 1f;
            return;
        }

        if (value > 1f + Epsilon)
        {
            _boost = Math.Max(_boost, value);
        }
        else if (_forced || (_boost > 1f + Epsilon && _lastTarget > 1f + Epsilon && Math.Abs(value - 1f) < Epsilon))
        {
            // The game just ended the sprint boost while sprint is still held: keep it.
            game.Write(target, _boost);
            _forced = true;
            value = _boost;
        }
        _lastTarget = value;
    }

    /// <summary>Address of the top speed-modifier's target field, or 0 if the stack is empty.</summary>
    private static nint TopModifierTarget(GameProcess game, nint player)
    {
        if (!game.TryRead<ushort>(player + Offsets.Player.SpeedModCount, out var count) || count == 0)
            return 0;
        nint array = game.ReadPointer(player + Offsets.Player.SpeedModArray);
        return array == 0 ? 0 : array + (count - 1) * Offsets.Player.SpeedModStride + 4;
    }

    protected override void Restore(GameProcess game, nint player)
    {
        game.Write<byte>(player + Offsets.Player.StaminaFreeze, _origFreeze ? (byte)1 : (byte)0);
        if (_forced && TopModifierTarget(game, player) is var t and not 0)
            game.Write(t, 1f);
        _forced = false;
    }
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

/// <summary>Scales movement speed through the engine's g_player_speed_scale variable (1.0 is normal).</summary>
public sealed class FastMovement : Feature
{
    private const float Speed = 1.75f;
    private float _original = 1f;

    public override string Name => "Fast movement";
    public override bool NeedsPlayer => false;

    protected override void OnEnable(GameProcess game, nint player)
    {
        if (game.TryRead<float>(game.Base + Offsets.PlayerSpeedScale, out var current))
            _original = current;
    }

    protected override void Apply(GameProcess game, nint player) =>
        game.Write(game.Base + Offsets.PlayerSpeedScale, Speed);

    protected override void Restore(GameProcess game, nint player) =>
        game.Write(game.Base + Offsets.PlayerSpeedScale, _original);
}

/// <summary>Jump height multiplier (1.0 is normal).</summary>
public sealed class HighJump() : FloatOverride(Offsets.Player.BaseJump, 2.25f)
{
    public override string Name => "High jump";
}
