using MetroExodusTrainer.Core.Features;

namespace MetroExodusTrainer.Core;

/// <summary>A named category of features.</summary>
public sealed record FeatureGroup(string Name, IReadOnlyList<Feature> Features);

/// <summary>Owns the feature list and applies enabled features against the attached game.</summary>
public sealed class Trainer
{
    /// <summary>Features by category, in display order. Hotkey slots follow this order.</summary>
    // Slot = fixed hotkey (0-8 Num1-9, 9-17 Ctrl+Num1-9, 18-26 Alt+Num1-9). Never renumber: new features take the next free slot.
    public IReadOnlyList<FeatureGroup> Groups { get; } =
    [
        new("Player",
        [
            new Invulnerability { Slot = 0 }, new InfiniteStamina { Slot = 1 }, new FastMovement { Slot = 2 },
            new HighJump { Slot = 3 }, new FlyMode { Slot = 21 },
        ]),
        new("Items",
        [
            new UnlimitedItems { Slot = 4 }, new FreeGasMask { Slot = 5 }, new InfiniteCraftResources { Slot = 6 },
            new GasMaskNeverBreaks { Slot = 18 }, new UnlimitedBattery { Slot = 19 },
        ]),
        new("Weapons",
        [
            new InfiniteMagazine { Slot = 7 }, new NoOverheat { Slot = 8 }, new NoJam { Slot = 9 },
            new NoRecoil { Slot = 10 }, new MaxAccuracy { Slot = 11 }, new RapidFire { Slot = 12 },
            new AlwaysClean { Slot = 13 }, new AirPressureFull { Slot = 20 },
        ]),
        new("Enemies", [new Invisibility { Slot = 14 }, new LowEnemyHealth { Slot = 15 }, new Deafen { Slot = 16 }]),
        new("World", [new FreezeTimeOfDay { Slot = 17 }]),
    ];

    public IReadOnlyList<Feature> Features { get; }

    /// <summary>Switches off the features whose game addresses or class tables are not available in the attached build.</summary>
    public void ApplySupport(BuildSupport support)
    {
        foreach (var feature in Features)
        {
            string? missing = feature.Requires.FirstOrDefault(r => r == "Vtables" ? !support.VtablesAvailable : support.IsMissing(r));
            feature.Unavailable = missing is null ? null
                : missing == "Vtables" ? "Needs the class tables of a known game build"
                : $"Not found in this game build ({missing})";
            if (feature.Unavailable is not null)
                feature.Set(false);
        }
    }

    public Trainer() => Features = [.. Groups.SelectMany(g => g.Features)];

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
