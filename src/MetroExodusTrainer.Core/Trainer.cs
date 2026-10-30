using MetroExodusTrainer.Core.Features;

namespace MetroExodusTrainer.Core;

/// <summary>Owns the feature list and applies enabled features against the attached game.</summary>
public sealed class Trainer
{
    public IReadOnlyList<Feature> Features { get; } =
    [
        new Invulnerability(),
        new InfiniteStamina(),
        new FastMovement(),
        new HighJump(),
        new UnlimitedItems(),
        new FreeGasMask(),
    ];

    /// <summary>Address of the player found by the last tick, or 0.</summary>
    public nint Player { get; private set; }

    public bool PlayerFound => Player != 0;

    /// <summary>
    /// Switches every feature off, then writes the game's real default values (even for features that were off,
    /// which fixes values left behind by an earlier run that was closed while a feature was on).
    /// </summary>
    public void ResetAll(GameProcess game)
    {
        foreach (var feature in Features)
            feature.Set(false);
        Tick(game);
        foreach (var feature in Features)
        {
            if (PlayerFound || !feature.NeedsPlayer)
                feature.ResetToDefaults(game, Player);
        }
        foreach (var feature in Features)
            feature.Shutdown(game);
    }

    public void Tick(GameProcess game)
    {
        nint player = PlayerLocator.Find(game);
        Player = player;
        foreach (var feature in Features)
        {
            if (PlayerFound || !feature.NeedsPlayer)
                feature.Tick(game, player);
        }
    }
}
