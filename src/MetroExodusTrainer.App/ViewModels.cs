using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MetroExodusTrainer.Core;
using MetroExodusTrainer.Core.Features;

namespace MetroExodusTrainer.Gui;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Tells the bindings that every property may have changed.</summary>
    public void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>One row: a feature, its hotkey label and its on/off state.</summary>
public sealed class FeatureVm(Feature feature, string hotkey, TrainerRunner runner) : Observable
{
    public string Name => feature.Name;
    public string Hotkey => hotkey;
    public string? Warning => feature.Warning;
    public bool HasWarning => feature.Warning is not null;
    public bool CanToggle => runner.CanToggle;
    public bool IsOn => feature.Enabled;

    public void Toggle()
    {
        if (runner.CanToggle)
            feature.Toggle();
        RaiseAll();
    }
}

public sealed class GroupVm(string name, IReadOnlyList<FeatureVm> items)
{
    public string Name => name;
    public IReadOnlyList<FeatureVm> Items => items;
}

public sealed class MainVm : Observable
{
    private static readonly Brush Ok = Frozen(0x3D, 0xDC, 0x97);
    private static readonly Brush Wait = Frozen(0xF5, 0xB9, 0x4A);
    private static readonly Brush Bad = Frozen(0xFF, 0x5C, 0x6C);

    private readonly TrainerRunner _runner;

    public MainVm(TrainerRunner runner)
    {
        _runner = runner;
        int index = 0;
        Groups =
        [
            .. runner.Trainer.Groups.Select(g => new GroupVm(g.Name,
                [.. g.Features.Select(f => new FeatureVm(f, runner.HotkeyLabel(index++), runner))])),
        ];
    }

    public IReadOnlyList<GroupVm> Groups { get; }

    public string ResetHotkey => _runner.ResetLabel;

    public string StatusTitle => _runner.State switch
    {
        RunnerState.WaitingForGame => "Waiting for Metro Exodus",
        RunnerState.UnsupportedBuild => "Unsupported game version",
        _ => _runner.Trainer.PlayerFound ? "Connected" : "Connected, no player yet",
    };

    public string StatusDetail => _runner.State switch
    {
        RunnerState.WaitingForGame => "Start the game; the trainer attaches by itself.",
        RunnerState.UnsupportedBuild => $"Found {_runner.Build?.FileVersion}, expected {Offsets.KnownFileVersion}. Features are disabled.",
        _ => _runner.Trainer.PlayerFound ? $"Game version {_runner.Build?.FileVersion}" : "Load a save (not a cutscene or menu).",
    };

    public Brush StatusBrush => _runner.State switch
    {
        RunnerState.WaitingForGame => Wait,
        RunnerState.UnsupportedBuild => Bad,
        _ => _runner.Trainer.PlayerFound ? Ok : Wait,
    };

    public bool NumLockOff => !Hotkeys.NumLockOn;

    public void Reset() => _runner.RequestReset();

    public void Refresh()
    {
        RaiseAll();
        foreach (var f in Groups.SelectMany(g => g.Items))
            f.RaiseAll();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
