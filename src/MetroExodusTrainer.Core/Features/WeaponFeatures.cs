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

    protected override void ApplyTo(GameProcess game, nint weapon) =>
        game.Write(weapon + Offsets.Weapon.Heat, 0f);
}

/// <summary>Keeps every weapon in the "not jammed" state.</summary>
public sealed class NoJam : WeaponFieldFeature
{
    public override string Name => "No weapon jam";

    protected override void ApplyTo(GameProcess game, nint weapon) =>
        game.Write<byte>(weapon + Offsets.Weapon.JamState, 0);
}

/// <summary>Clears the recoil accumulators so a shot does not kick the aim.</summary>
public sealed class NoRecoil : WeaponFieldFeature
{
    public override string Name => "No recoil";

    protected override void ApplyTo(GameProcess game, nint weapon)
    {
        game.Write(weapon + Offsets.Weapon.RecoilLevel, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilVertical, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilHorizontal, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilShake, 0f);
        game.Write(weapon + Offsets.Weapon.RecoilHorizRandom, 0f);
    }
}

/// <summary>Clears the dynamic spread (movement and firing) of every weapon.</summary>
public sealed class MaxAccuracy : WeaponFieldFeature
{
    public override string Name => "Maximum shoot accuracy";

    protected override void ApplyTo(GameProcess game, nint weapon) =>
        game.Write(weapon + Offsets.Weapon.CurrentDispersion, 0f);
}

/// <summary>Halves the time between shots; the original interval is put back when switched off.</summary>
public sealed class RapidFire : Feature
{
    private const float Factor = 0.5f;
    private const float MaxPlausibleInterval = 5f;

    // Original shot interval per weapon, captured the first time the weapon is seen.
    private readonly Dictionary<nint, float> _original = [];

    public override string Name => "Rapid fire";

    protected override void OnEnable(GameProcess game, nint player) => _original.Clear();

    protected override void Apply(GameProcess game, nint player)
    {
        foreach (nint weapon in Firearms.Find(game, player))
        {
            nint address = weapon + Offsets.Weapon.ShotInterval;
            if (!game.TryRead<float>(address, out var current) || current <= 0f || current > MaxPlausibleInterval)
                continue;

            // A value we wrote ourselves is current == original * Factor; anything else is the game's own value.
            if (!_original.TryGetValue(weapon, out var original) || Math.Abs(current - original * Factor) > 1e-4f)
                _original[weapon] = original = current;
            game.Write(address, original * Factor);
        }
    }

    protected override void Restore(GameProcess game, nint player)
    {
        foreach (var (weapon, original) in _original)
            game.Write(weapon + Offsets.Weapon.ShotInterval, original);
        _original.Clear();
    }
}
