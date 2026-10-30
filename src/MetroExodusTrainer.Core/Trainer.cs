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
    ];

    /// <summary>Whether the last tick found the player entity.</summary>
    public bool PlayerFound { get; private set; }

    public void Tick(GameProcess game)
    {
        nint player = PlayerLocator.Find(game);
        PlayerFound = player != 0;
        if (!PlayerFound)
            return;
        foreach (var feature in Features)
            feature.Tick(game, player);
    }
}
