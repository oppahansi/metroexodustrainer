namespace MetroExodusTrainer.Core.Features;

/// <summary>
/// Enemies stop treating the player as a target: <c>cplayer::is_target</c> returns "not a target" while bit 2 of
/// the developer cheat flags is set.
/// </summary>
public sealed class Invisibility() : ActorFlagFeature(Offsets.ActorFlags.NotATarget)
{
    public override string Name => "Invisible to enemies";
}

/// <summary>
/// Keeps hostile NPCs (a faction different from the player's, as the game decides it) at a sliver of health so any hit kills them, even a limb shot whose damage is scaled down. Allies, neutral
/// animals and the player are never touched, and dead NPCs (health 0) are not revived.
/// </summary>
public sealed class LowEnemyHealth : Feature
{
    private const float Low = 0.001f;

    public override string Name => "Low enemy health";

    // Enemies are only scanned a few times per second; the entity list is large.
    private long _lastScan;
    private const long ScanEveryMs = 100;

    // Many human enemies sit at 1000-1001 until they act (a scripted health boost); treat that as full health too.
    private static bool IsBoosted(float health) => health is >= 999f and <= 1002f;

    protected override void Apply(GameProcess game, nint player)
    {
        long now = Environment.TickCount64;
        if (now - _lastScan < ScanEveryMs)
            return;
        _lastScan = now;

        if (!game.TryRead<byte>(player + Offsets.Entities.FriendType, out var faction))
            return;

        foreach (nint npc in Npcs.Find(game))
        {
            if (!Npcs.IsEnemy(game, npc, faction))
                continue;
            if (game.TryRead<float>(npc + Offsets.Entities.Health, out var health) && health > Low && (health <= 1.5f || IsBoosted(health)))
                game.Write(npc + Offsets.Entities.Health, Low);
        }
    }
}

/// <summary>
/// NPCs no longer hear the player: sounds whose source is the player are dropped where NPCs take sounds in.
/// Other sounds, and sight, are unaffected.
/// </summary>
public sealed class Deafen : Feature
{
    private readonly PlayerSoundHook _hook = new();

    public override string Name => "Enemies cannot hear you";

    protected override void OnEnable(GameProcess game, nint player) =>
        Warning = _hook.TryApply(game, player) ? null : _hook.Error;

    protected override void Apply(GameProcess game, nint player)
    {
        if (_hook.Applied)
            _hook.SetPlayer(game, player);
    }

    protected override void Restore(GameProcess game, nint player)
    {
        _hook.Revert(game);
        Warning = null;
    }

    protected override void OnShutdown(GameProcess game) => _hook.Revert(game);
}
