namespace MetroExodusTrainer.Core.Features;

/// <summary>A toggleable trainer function that is re-applied on every tick while enabled.</summary>
public abstract class Feature
{
    public abstract string Name { get; }
    public bool Enabled { get; private set; }

    /// <summary>Called on the tick after the feature is switched on, before the first <see cref="Apply"/>.</summary>
    protected virtual void OnEnable(GameProcess game, nint player) { }

    /// <summary>Called while enabled, every tick, with a valid player address.</summary>
    protected abstract void Apply(GameProcess game, nint player);

    /// <summary>Called once when the feature is switched off, to put back what it changed.</summary>
    protected virtual void Restore(GameProcess game, nint player) { }

    private bool _needsEnable;
    private bool _needsRestore;

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
