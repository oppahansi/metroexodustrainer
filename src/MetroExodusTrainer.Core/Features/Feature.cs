namespace MetroExodusTrainer.Core.Features;

/// <summary>A toggleable trainer function that is re-applied on every tick while enabled.</summary>
public abstract class Feature
{
    public abstract string Name { get; }

    /// <summary>Names of the game addresses (or "Vtables") this feature needs; see <see cref="AddressBook"/>.</summary>
    public virtual IReadOnlyList<string> Requires => [];

    /// <summary>Why the feature cannot be used with the attached game build, or null.</summary>
    public string? Unavailable { get; internal set; }

    /// <summary>Fixed hotkey slot of this feature (see <see cref="Hotkeys.ForFeature"/>); never reused, so keys stay stable.</summary>
    public int Slot { get; init; } = -1;
    public bool Enabled { get; private set; }

    /// <summary>Set when the feature could not fully apply (for example, a code patch was refused).</summary>
    public string? Warning { get; protected set; }

    /// <summary>False for features that only touch globals, so they keep working (and restore) without a player entity.</summary>
    public virtual bool NeedsPlayer => true;

    /// <summary>Called on the tick after the feature is switched on, before the first <see cref="Apply"/>.</summary>
    protected virtual void OnEnable(GameProcess game, nint player) { }

    /// <summary>Called while enabled, every tick, with a valid player address.</summary>
    protected abstract void Apply(GameProcess game, nint player);

    /// <summary>Called once when the feature is switched off, to put back what it changed.</summary>
    protected virtual void Restore(GameProcess game, nint player) { }

    private bool _needsEnable;
    private bool _needsRestore;

    /// <summary>Writes the game's real default values, whether or not this feature is switched on.</summary>
    internal void ResetToDefaults(GameProcess game, nint player) => Defaults(game, player);

    protected virtual void Defaults(GameProcess game, nint player) { }

    /// <summary>Last chance to undo changes that are not tied to the player entity, e.g. code patches.</summary>
    internal void Shutdown(GameProcess game) => OnShutdown(game);

    protected virtual void OnShutdown(GameProcess game) { }

    public void Toggle() => Set(!Enabled);

    public void Set(bool enabled)
    {
        if (enabled == Enabled)
            return;
        Enabled = enabled;
        if (enabled) _needsEnable = true;
        else _needsRestore = true;
    }

    internal void Tick(GameProcess game, nint player)
    {
        if (_needsRestore)
        {
            _needsRestore = false;
            _needsEnable = false;
            Restore(game, player);
        }
        if (!Enabled)
            return;
        if (_needsEnable)
        {
            _needsEnable = false;
            OnEnable(game, player);
        }
        Apply(game, player);
    }
}
