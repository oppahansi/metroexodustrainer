namespace MetroExodusTrainer.Core.Features;

/// <summary>
/// Stops the in-game clock by holding the day cycle's base speed multiplier (game seconds per real second,
/// 12 in the main game) at 0. It is only touched when the day cycle looks valid (day end after day start, value in a
/// sane range); the value seen when the feature was switched on is put back afterwards.
/// </summary>
public sealed class FreezeTimeOfDay : Feature
{
    private const float DefaultSpeed = 12f;
    private const float MaxSaneSpeed = 1000f;

    private float _original = DefaultSpeed;

    public override string Name => "Freeze time of day";

    public override IReadOnlyList<string> Requires => ["GGame"];

    public override bool NeedsPlayer => false;

    protected override void OnEnable(GameProcess game, nint player)
    {
        _original = TryAddress(game, out var speedAddress) && game.TryRead<float>(speedAddress, out var speed) && speed > 0f
            ? speed
            : DefaultSpeed;
    }

    protected override void Apply(GameProcess game, nint player)
    {
        if (TryAddress(game, out var speedAddress))
            game.Write(speedAddress, 0f);
    }

    protected override void Restore(GameProcess game, nint player)
    {
        if (TryAddress(game, out var speedAddress))
            game.Write(speedAddress, _original);
    }

    protected override void Defaults(GameProcess game, nint player)
    {
        // Only a frozen clock is ours to fix; any other value is the game's own and stays.
        if (TryAddress(game, out var speedAddress) && game.TryRead<float>(speedAddress, out var speed) && speed == 0f)
            game.Write(speedAddress, DefaultSpeed);
    }

    private static bool TryAddress(GameProcess game, out nint speedAddress)
    {
        speedAddress = 0;
        nint gameObject = game.ReadPointer(game.Base + Offsets.World.GGame);
        if (gameObject == 0)
            return false;

        nint cycle = gameObject + Offsets.World.DayCycle;
        if (!game.TryRead<ulong>(cycle + Offsets.World.DayStart, out var start) ||
            !game.TryRead<ulong>(cycle + Offsets.World.DayEnd, out var end) || end <= start)
            return false;
        if (!game.TryRead<float>(cycle + Offsets.World.DayBaseMult, out var speed) || float.IsNaN(speed) || speed < 0f || speed > MaxSaneSpeed)
            return false;

        speedAddress = cycle + Offsets.World.DayBaseMult;
        return true;
    }
}
