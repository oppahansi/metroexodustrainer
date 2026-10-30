using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core.Features;

/// <summary>Writes the same value into a field of every firearm in the inventory each tick.</summary>
public abstract class WeaponFieldFeature : Feature
{
    protected abstract void ApplyTo(GameProcess game, nint weapon);

    protected override void Apply(GameProcess game, nint player)
    {
        foreach (nint weapon in Firearms.Find(game, player))
            ApplyTo(game, weapon);
    }
}

/// <summary>Keeps the heat counter at zero so a weapon never reaches its critical heat.</summary>
public sealed class NoOverheat : WeaponFieldFeature
{
    public override string Name => "No weapon overheat";

    public override IReadOnlyList<string> Requires => ["Vtables"];

    protected override void ApplyTo(GameProcess game, nint weapon) =>
        game.Write(weapon + Offsets.Weapon.Heat, 0f);
}

/// <summary>
/// Keeps every firearm clean. The weapon's dirt level is a half-float at +0xB0 (0 = clean, 1 = filthy); the game turns
/// it into accuracy and jam penalties (<c>apply_dirt_modifiers</c>) and the workbench cleaning lowers it.
/// There is nothing to restore: a clean weapon simply stays clean.
/// </summary>
public sealed class AlwaysClean : WeaponFieldFeature
{
    public override string Name => "Weapons never get dirty";

    public override IReadOnlyList<string> Requires => ["Vtables"];

    protected override void ApplyTo(GameProcess game, nint weapon)
    {
        if (game.TryRead<ushort>(weapon + Offsets.Weapon.Dirt, out var dirt) && dirt != 0)
            game.Write<ushort>(weapon + Offsets.Weapon.Dirt, 0);
    }
}

/// <summary>
/// Keeps the air pressure of pneumatic weapons (Tikhar, Hellbreath, Helsing, ...) in the high band, so they never need
/// pumping. The weapon's static data holds three bounds (low, high, cap); the pressure is only ever raised to the middle
/// of the high band and the bounds must look sane first.
/// </summary>
public sealed class AirPressureFull : WeaponFieldFeature
{
    public override string Name => "Air weapons stay pressurized";

    public override IReadOnlyList<string> Requires => ["Vtables"];

    protected override void ApplyTo(GameProcess game, nint weapon)
    {
        if (!PneumoWeaponVtables.Rvas.Contains(game.ReadPointer(weapon) - game.Base))
            return;

        nint parameters = game.ReadPointer(weapon + Offsets.Inventory.EntityStaticParams);
        if (parameters == 0)
            return;

        nint block = parameters - Offsets.Pneumo.StaticParamsBias;
        if (!game.TryRead<float>(block + Offsets.Pneumo.HighBound, out var high) ||
            !game.TryRead<float>(block + Offsets.Pneumo.CapBound, out var cap) ||
            !(high > 0f && cap > high && cap < 1000f))
            return;

        float target = high + (cap - high) * 0.5f;
        foreach (nint field in new[] { weapon + Offsets.Pneumo.Pressure, weapon + Offsets.Pneumo.RealPressure })
            if (game.TryRead<float>(field, out var current) && current >= 0f && current < target)
                game.Write(field, target);
    }
}

/// <summary>Keeps every weapon in the "not jammed" state.</summary>
public sealed class NoJam : WeaponFieldFeature
{
    public override string Name => "No weapon jam";

    public override IReadOnlyList<string> Requires => ["Vtables"];

    protected override void ApplyTo(GameProcess game, nint weapon) =>
        game.Write<byte>(weapon + Offsets.Weapon.JamState, 0);
}

/// <summary>
/// Removes weapon recoil. <c>recoil_curve_evaluate</c> is the one function that turns the recoil level into the
/// vertical, horizontal and shake kick, so it is patched to return 0 (<c>xorps xmm0,xmm0; ret</c>). Writing the
/// fields from outside is too slow: the game recomputes them every frame.
/// </summary>
public sealed class NoRecoil : WeaponFieldFeature
{
    private readonly CodePatch _curvePatch = new(
        "recoil curve", () => Offsets.Weapon.RecoilCurveEvaluate,
        expected: [0x48, 0x89, 0x5C, 0x24, 0x08],
        replacement: [0x0F, 0x57, 0xC0, 0xC3, 0x90]);

    public override string Name => "No recoil";

    public override IReadOnlyList<string> Requires => ["RecoilCurveEvaluate", "Vtables"];

    protected override void OnEnable(GameProcess game, nint player) =>
        Warning = _curvePatch.TryApply(game) ? null : _curvePatch.Error;

    protected override void Restore(GameProcess game, nint player)
    {
        _curvePatch.Revert(game);
        Warning = null;
    }

    protected override void OnShutdown(GameProcess game) => _curvePatch.Revert(game);

    protected override void ApplyTo(GameProcess game, nint weapon)
    {
        game.Write(weapon + Offsets.Weapon.RecoilLevel, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilVertical, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilHorizontal, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilShake, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilHorizRandom, 0f);
    }
}

/// <summary>
/// Removes the shot spread. <c>weapon::fire_dispersion</c> returns the spread as a float in xmm0, so it is patched
/// to return 0 (<c>xorps xmm0,xmm0; ret</c>); the game recomputes the spread every shot, so field writes are too late.
/// </summary>
public sealed class MaxAccuracy : Feature
{
    private readonly CodePatch _patch = new(
        "fire dispersion", () => Offsets.Weapon.FireDispersion,
        expected: [0x48, 0x8B, 0xC4, 0x55, 0x53],
        replacement: [0x0F, 0x57, 0xC0, 0xC3, 0x90]);

    public override string Name => "Maximum shoot accuracy";

    public override IReadOnlyList<string> Requires => ["FireDispersion"];

    protected override void OnEnable(GameProcess game, nint player) =>
        Warning = _patch.TryApply(game) ? null : _patch.Error;

    protected override void Apply(GameProcess game, nint player) { }

    protected override void Restore(GameProcess game, nint player)
    {
        _patch.Revert(game);
        Warning = null;
    }

    protected override void OnShutdown(GameProcess game) => _patch.Revert(game);
}

/// <summary>
/// Shortens the time between shots. The interval is computed in <c>calc_shot_interval</c>; a hook scales its result,
/// so the game's own value is never overwritten and switching off simply puts the factor back to 1.
/// </summary>
public sealed class RapidFire : Feature
{
    private const float Factor = 0.5f;

    private readonly ShotIntervalHook _hook = new();

    public override string Name => "Rapid fire";

    public override IReadOnlyList<string> Requires => ["CalcShotIntervalReturn"];

    protected override void OnEnable(GameProcess game, nint player) =>
        Warning = _hook.TryApply(game, Factor) ? null : _hook.Error;

    protected override void Apply(GameProcess game, nint player) { }

    protected override void Restore(GameProcess game, nint player)
    {
        _hook.Revert(game);
        Warning = null;
    }

    protected override void OnShutdown(GameProcess game) => _hook.Revert(game);
}
