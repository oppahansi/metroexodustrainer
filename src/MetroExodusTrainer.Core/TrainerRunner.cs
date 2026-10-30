using System.Runtime.InteropServices;

namespace MetroExodusTrainer.Core;

public enum RunnerState { WaitingForGame, Running, UnsupportedBuild }

/// <summary>
/// Runs the trainer on a background thread: waits for the game, re-attaches if it restarts, polls the hotkeys and
/// ticks the features. Front-ends read the public state and call <see cref="RequestReset"/>; stopping the runner
/// puts the game back to its defaults.
/// </summary>
public sealed class TrainerRunner : IDisposable
{
    private const int TickMs = 10;

    private readonly Hotkey[] _featureKeys;
    private readonly Hotkeys.Debouncer[] _featureDebounce;
    private readonly bool[] _featureDown;
    private readonly Hotkeys.Debouncer _resetDebounce = new();
    private bool _resetDown;
    private volatile bool _resetRequested;
    private volatile bool _stop;
    private Thread? _thread;
    private GameProcess? _game;

    public TrainerRunner()
    {
        Trainer = new Trainer();
        int n = Trainer.Features.Count;
        _featureKeys = [.. Trainer.Features.Select(f => Hotkeys.ForFeature(f.Slot))];
        _featureDebounce = [.. _featureKeys.Select(_ => new Hotkeys.Debouncer())];
        _featureDown = new bool[n];
    }

    public Trainer Trainer { get; }

    public RunnerState State { get; private set; } = RunnerState.WaitingForGame;

    public BuildInfo? Build { get; private set; }

    /// <summary>The hotkey label of each feature, in <see cref="Trainer.Features"/> order.</summary>
    public string HotkeyLabel(int featureIndex) => _featureKeys[featureIndex].Label;

    public string ResetLabel => Hotkeys.Reset.Label;

    public bool Attached => State is RunnerState.Running or RunnerState.UnsupportedBuild;

    public bool CanToggle => State == RunnerState.Running;

    public void Start()
    {
        if (_thread is not null)
            return;
        timeBeginPeriod(1);
        _thread = new Thread(Loop) { IsBackground = true, Name = "trainer" };
        _thread.Start();
    }

    /// <summary>Asks the loop to switch everything off and write the default values.</summary>
    public void RequestReset() => _resetRequested = true;

    /// <summary>Stops the loop and, if the game is still running, restores its default values.</summary>
    public void Stop()
    {
        _stop = true;
        _thread?.Join(2000);
        _thread = null;
        RestoreDefaults();
        _game?.Dispose();
        _game = null;
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        while (!_stop)
        {
            State = RunnerState.WaitingForGame;
            Build = null;
            var game = WaitForGame();
            if (game is null)
                return;

            _game = game;
            Build = BuildInfo.FromExe(game.ExePath);
            bool buildOk = Build.FileVersion == Offsets.KnownFileVersion;
            State = buildOk ? RunnerState.Running : RunnerState.UnsupportedBuild;

            while (!_stop && !game.HasExited)
            {
                Hotkeys.Update();
                for (int i = 0; i < _featureKeys.Length; i++)
                {
                    bool pressed = _featureDebounce[i].Held(_featureKeys[i].IsDown());
                    if (pressed && !_featureDown[i] && buildOk)
                        Trainer.Features[i].Toggle();
                    _featureDown[i] = pressed;
                }

                bool resetPressed = _resetDebounce.Held(Hotkeys.Reset.IsDown());
                bool reset = (resetPressed && !_resetDown) || _resetRequested;
                _resetDown = resetPressed;
                _resetRequested = false;

                if (buildOk)
                {
                    if (reset)
                        Trainer.ResetAll(game);
                    else
                        Trainer.Tick(game);
                }
                Thread.Sleep(TickMs);
            }

            // Game closed (or we are stopping): forget the feature state; Stop() restores a live game.
            if (game.HasExited)
            {
                foreach (var f in Trainer.Features) f.Set(false);
                _game = null;
                game.Dispose();
            }
        }
    }

    private GameProcess? WaitForGame()
    {
        while (!_stop)
        {
            var game = GameProcess.Attach();
            if (game is not null)
                return game;
            Thread.Sleep(1000);
        }
        return null;
    }

    private void RestoreDefaults()
    {
        var game = _game;
        if (game is null || game.HasExited || State != RunnerState.Running)
            return;
        try { Trainer.ResetAll(game); } catch { /* process went away while shutting down */ }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);
}
