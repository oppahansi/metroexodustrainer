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

    protected override void Defaults(GameProcess game, nint player)
    {
        game.SetBit(player + Offsets.Player.Invulnerable, Offsets.Player.InvulnerableBit, false);
        game.SetBit(player + Offsets.Player.Immortal, Offsets.Player.ImmortalBit, false);
    }
}

/// <summary>
/// Stops stamina drain and the sprint "exhaustion". Stamina itself is not what ends a long sprint:
/// <list type="bullet">
/// <item>The freeze flag makes update_stamina take its regeneration path; stamina is topped up every tick and the
/// "can use stamina" flag (cleared by use_stamina when exhausted) is kept set.</item>
/// <item>The exhaustion script clears the sprint-allowed byte for a frame, which zeroes the sprint multiplier and
/// cancels the sprint before any external write can intervene, so the store of that byte is disabled.</item>
/// <item>Sprinting pushes a speed modifier (target 1.5) that a script block removes after ~13 s, which is the
/// slowdown players feel. While sprint is held that block's removal is suppressed by a hook, and the entry is
/// released again when sprint ends.</item>
/// </list>
/// </summary>
public sealed class InfiniteStamina : Feature
{
    private const float Epsilon = 0.001f;

    private bool _origFreeze;
    private float _boost = 1f;       // highest sprint-boost target seen while sprinting
    private float _lastTarget = 1f;  // top modifier target on the previous tick
    private long _owner;            // script block that owns the boost entry
    private bool _forced;            // we put the boost target back after the game lowered it
    private bool _hadBoost;          // the boost entry was seen during the sprint that is currently held

    private readonly CodePatch _sprintAllowedPatch = new(
        "sprint-allowed store", Offsets.SprintAllowedStore,
        expected: [0x88, 0x81, 0x2A, 0x25, 0x00, 0x00],
        replacement: [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private readonly SprintBoostHook _boostHook = new();

    public override string Name => "Infinite stamina";

    protected override void OnEnable(GameProcess game, nint player)
    {
        _origFreeze = game.Read<byte>(player + Offsets.Player.StaminaFreeze) != 0;
        _boost = _lastTarget = 1f;
        _owner = 0;
        _forced = _hadBoost = false;

        Warning = !_sprintAllowedPatch.TryApply(game) ? _sprintAllowedPatch.Error
                : !_boostHook.TryApply(game) ? _boostHook.Error
                : null;
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

        if (!sprinting)
        {
            // Sprint ended: the hook may have kept the boost entry alive, so let the game finish it
            // (ramp down and remove) if it is still boosted.
            if (target != 0 && game.TryRead<float>(target, out var v) && v > 1f + Epsilon && (_hadBoost || _forced))
                ReleaseBoost(game, target);
            _forced = _hadBoost = false;
            _lastTarget = 1f;
            return;
        }

        if (target == 0 || !game.TryRead<float>(target, out var value))
        {
            _lastTarget = 1f;
            return;
        }

        if (value > 1f + Epsilon)
        {
            _boost = Math.Max(_boost, value);
            _hadBoost = true;
            if (game.TryRead<long>(target + 12, out var owner) && owner != 0 && owner != _owner)
            {
                _owner = owner;
                _boostHook.SetOwner(game, owner);
            }
        }
        else if (_forced || (_boost > 1f + Epsilon && _lastTarget > 1f + Epsilon && Math.Abs(value - 1f) < Epsilon))
        {
            // The game lowered the boost target while sprint is still held: put it back.
            game.Write(target, _boost);
            _forced = true;
            value = _boost;
        }
        _lastTarget = value;
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
        if (TopModifierTarget(game, player) is var t and not 0 && game.TryRead<float>(t, out var v) && v > 1f + Epsilon)
            ReleaseBoost(game, t);
        _forced = _hadBoost = false;
        _sprintAllowedPatch.Revert(game);
        _boostHook.Revert(game);
        Warning = null;
    }

    protected override void Defaults(GameProcess game, nint player) =>
        game.Write<byte>(player + Offsets.Player.StaminaFreeze, 0);

    protected override void OnShutdown(GameProcess game)
    {
        _sprintAllowedPatch.Revert(game);
        _boostHook.Revert(game);
    }
}

/// <summary>Scales movement speed through the engine's g_player_speed_scale variable (default 1.25).</summary>
public sealed class FastMovement : Feature
{
    private const float Speed = 1.75f;
    private float _original = Offsets.DefaultPlayerSpeedScale;

    public override string Name => "Fast movement";
    public override bool NeedsPlayer => false;

    protected override void OnEnable(GameProcess game, nint player)
    {
        // A value equal to our own override means an earlier run left it behind; use the real default then.
        _original = game.TryRead<float>(game.Base + Offsets.PlayerSpeedScale, out var current) &&
                    Math.Abs(current - Speed) > 0.001f
            ? current
            : Offsets.DefaultPlayerSpeedScale;
    }

    protected override void Apply(GameProcess game, nint player) =>
        game.Write(game.Base + Offsets.PlayerSpeedScale, Speed);

    protected override void Restore(GameProcess game, nint player) =>
        game.Write(game.Base + Offsets.PlayerSpeedScale, _original);

    protected override void Defaults(GameProcess game, nint player) =>
        game.Write(game.Base + Offsets.PlayerSpeedScale, Offsets.DefaultPlayerSpeedScale);
}

/// <summary>Jump height multiplier. The real default comes from the per-suit table, not from memory at switch-on.</summary>
public sealed class HighJump : Feature
{
    private const float Jump = 2.25f;

    public override string Name => "High jump";

    protected override void Apply(GameProcess game, nint player) =>
        game.Write(player + Offsets.Player.BaseJump, Jump);

    protected override void Restore(GameProcess game, nint player) => Defaults(game, player);

    protected override void Defaults(GameProcess game, nint player)
    {
        float jump = SuitDefaults.TryRead(game, player, out _, out var suitJump) && suitJump > 0f ? suitJump : 1f;
        game.Write(player + Offsets.Player.BaseJump, jump);
    }
}

/// <summary>
/// Fly mode: the player floats and moves without gravity or collisions. A hook in the game's per-frame movement
/// (<see cref="PlayerFlyHook"/>) replaces the character's normal movement with a displacement from the fly velocity this
/// feature computes. W/A/S/D move along the look direction, Space goes up, C goes down, Left Shift is faster. Keys only
/// count while the game window has focus. Switching it off hands movement back to the game, so the player falls.
/// </summary>
public sealed class FlyMode : Feature
{
    private const float Speed = 9f;          // game units (metres) per second
    private const float BoostFactor = 3f;
    private const float Hover = 0.0025f;        // upward speed that keeps the body from falling
    private const float HoverThreshold = 0.01f;
    private const int VkW = 0x57, VkA = 0x41, VkS = 0x53, VkD = 0x44, VkC = 0x43, VkSpace = 0x20, VkLShift = 0xA0;

    private readonly PlayerFlyHook _hook = new();

    /// <summary>The movement hook, exposed for the trace tool.</summary>
    public PlayerFlyHook Hook => _hook;

    public override string Name => "Fly mode (WASD, Space up, C down, Shift fast)";

    private byte _jumpAllowedBefore = 1;

    protected override void OnEnable(GameProcess game, nint player)
    {
        Warning = _hook.TryApply(game, player) ? null : _hook.Error;
        if (game.TryRead<byte>(player + Offsets.Player.VsMoveAllowed + Offsets.Player.VsMoveAllowedJump, out var jump))
            _jumpAllowedBefore = jump;
    }

    protected override void Apply(GameProcess game, nint player)
    {
        if (!_hook.Applied)
            return;

        _hook.SetPlayer(game, player);

        float vx = 0, vy = 0, vz = 0;
        if (Hotkeys.ForegroundProcessId() == game.Process.Id &&
            game.TryRead<float>(player + Offsets.Fly.HeadPitch, out var pitch) &&
            game.TryRead<float>(player + Offsets.Fly.HeadYaw, out var yaw) &&
            float.IsFinite(pitch) && float.IsFinite(yaw))
        {
            // Pitch is negative when looking up; yaw 0 faces +Z. Forward follows the look direction, strafe is horizontal.
            float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch), sy = MathF.Sin(yaw), cy = MathF.Cos(yaw);
            (float fx, float fy, float fz) = (sy * cp, -sp, cy * cp);
            (float rx, float rz) = (cy, -sy);

            float forward = (Hotkeys.KeyDown(VkW) ? 1 : 0) - (Hotkeys.KeyDown(VkS) ? 1 : 0);
            float right = (Hotkeys.KeyDown(VkD) ? 1 : 0) - (Hotkeys.KeyDown(VkA) ? 1 : 0);
            float up = (Hotkeys.KeyDown(VkSpace) ? 1 : 0) - (Hotkeys.KeyDown(VkC) ? 1 : 0);

            float speed = Speed * (Hotkeys.KeyDown(VkLShift) ? BoostFactor : 1f);
            vx = (fx * forward + rx * right) * speed;
            vy = (fy * forward + up) * speed;
            vz = (fz * forward + rz * right) * speed;
        }

        // The physics body takes the requested velocity as input, but a downward component is ignored (gravity does that) and
        // a zero vertical component leaves gravity on. A tiny upward component (above the game's jump threshold of 0.001)
        // makes the body hold its height. To go down, gravity is let through only while the body is slower than wanted.
        float sendY = vy;
        if (vy > HoverThreshold)
            sendY = vy;
        else if (vy < -HoverThreshold && FallingSpeed(game) is float falling && falling > vy)
            sendY = 0f;
        else
            sendY = Hover;

        _hook.SetVelocity(game, vx, sendY, vz);
        _hook.SetEnabled(game, true);

        // The game's climb-over (vault) logic would otherwise start whenever flying into a ledge.
        game.Write<byte>(player + Offsets.Player.VsMoveAllowed + Offsets.Player.VsMoveAllowedJump, 0);
    }

    private float? FallingSpeed(GameProcess game)
    {
        nint mover = _hook.ReadMover(game);
        nint controller = mover == 0 ? 0 : game.ReadPointer(mover + 0x68);
        return controller != 0 && game.TryRead<float>(controller + Offsets.Fly.ControllerLastVelocityY, out var v) ? v : null;
    }

    protected override void Restore(GameProcess game, nint player)
    {
        game.Write(player + Offsets.Player.VsMoveAllowed + Offsets.Player.VsMoveAllowedJump, _jumpAllowedBefore);
        _hook.SetEnabled(game, false);
        _hook.Revert(game);
        Warning = null;
    }

    protected override void OnShutdown(GameProcess game) => _hook.Revert(game);
}
