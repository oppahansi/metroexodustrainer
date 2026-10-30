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
    private float _blend = 10f;      // blend rate of the boost entry, copied when re-creating it
    private long _owner;             // script block that owns the boost entry; update_xform removes owner-less settled entries
    private float _lastTarget = 1f;  // top modifier target on the previous tick
    private bool _forced;            // we overrode or re-created the boost entry and must give it back
    private bool _hadBoost;          // the boost was active earlier in the sprint that is currently held
    private int _heldTicks;          // consecutive ticks with the sprint flag set

    // Sprint-allowed script: it clears the sprint-allowed byte for a frame; update_xform then zeroes the sprint
    // multiplier and can_sprint fails before any external write can intervene, so the store is disabled.
    private readonly CodePatch _sprintAllowedPatch = new(
        "sprint-allowed store", Offsets.SprintAllowedStore,
        expected: [0x88, 0x81, 0x2A, 0x25, 0x00, 0x00],
        replacement: [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    public override string Name => "Infinite stamina";

    protected override void OnEnable(GameProcess game, nint player)
    {
        _origFreeze = game.Read<byte>(player + Offsets.Player.StaminaFreeze) != 0;
        _boost = _lastTarget = 1f;
        _blend = 10f;
        _owner = 0;
        _forced = _hadBoost = false;
        _heldTicks = 0;
        Warning = _sprintAllowedPatch.TryApply(game) ? null : _sprintAllowedPatch.Error;
    }

    protected override void Apply(GameProcess game, nint player)
    {
        game.Write<byte>(player + Offsets.Player.StaminaFreeze, 1);
        game.Write(player + Offsets.Player.Stamina, 1f);
        game.Write<byte>(player + Offsets.Player.CanUseStamina, 1);
        game.Write<byte>(player + Offsets.Player.VsMoveAllowed + Offsets.Player.VsMoveAllowedSprint, 1);
        KeepSprintBoost(game, player);
    }

    /// <summary>
    /// Sprinting runs a script block that pushes a speed modifier (target 1.5) and, after ~13 s, either sets its
    /// target back to 1.0 or removes it outright. While sprint is held that modifier is kept (or re-created).
    /// </summary>
    private void KeepSprintBoost(GameProcess game, nint player)
    {
        bool sprinting = game.TryRead<uint>(player + Offsets.Player.StateReal, out var state) &&
                         (state & Offsets.Player.SprintStateFlag) != 0;
        nint target = TopModifierTarget(game, player);

        if (!sprinting)
        {
            // Sprint released: hand control back to the game if we were holding the boost.
            if (_forced && target != 0)
                ReleaseBoost(game, target);
            _forced = _hadBoost = false;
            _lastTarget = 1f;
            _heldTicks = 0;
            return;
        }

        _heldTicks++;

        if (target == 0)
        {
            // The game removed the boost while sprint is still held (debounced so a fresh sprint can push its own).
            if (_hadBoost && _heldTicks >= 5)
                RecreateBoost(game, player);
            _lastTarget = 1f;
            return;
        }

        if (!game.TryRead<float>(target, out var value))
            return;

        if (value > 1f + Epsilon)
        {
            _boost = Math.Max(_boost, value);
            _hadBoost = true;
            if (game.TryRead<float>(target + 4, out var blend) && blend > 0f)
                _blend = blend;
            if (game.TryRead<long>(target + 12, out var owner) && owner != 0)
                _owner = owner;
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

    /// <summary>Re-creates the removed boost as the only stack entry (no script owner), at full strength.</summary>
    private void RecreateBoost(GameProcess game, nint player)
    {
        nint array = game.ReadPointer(player + Offsets.Player.SpeedModArray);
        if (array == 0 || !game.TryRead<ushort>(player + Offsets.Player.SpeedModCapacity, out var capacity) || capacity < 1)
        {
            Warning = "could not restore the sprint boost (modifier stack not allocated)";
            return;
        }

        if (_owner == 0)
        {
            // Without an owner the game removes the settled entry on the very next frame.
            Warning = "could not restore the sprint boost (owner of the entry was not seen)";
            return;
        }

        game.Write(array, _boost);            // current
        game.Write(array + 4, _boost);        // target
        game.Write(array + 8, _blend);        // blend
        game.Write(array + 12, 0f);
        game.Write<long>(array + 16, _owner); // same script block as the original entry; its next reset removes this one
        game.Write<ushort>(player + Offsets.Player.SpeedModCount, 1);
        _forced = true;
        _lastTarget = _boost;
    }

    /// <summary>Lets the game finish the boost: target back to 1.0 and no owner, so update_xform ramps it down and removes it.</summary>
    private static void ReleaseBoost(GameProcess game, nint targetAddress)
    {
        game.Write(targetAddress, 1f);
        game.Write<long>(targetAddress + 12, 0);
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
            ReleaseBoost(game, t);
        _forced = false;
        _sprintAllowedPatch.Revert(game);
        Warning = null;
    }

    protected override void OnShutdown(GameProcess game) => _sprintAllowedPatch.Revert(game);
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
