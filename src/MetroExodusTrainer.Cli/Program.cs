using System.Runtime.InteropServices;
using MetroExodusTrainer.Core;

// Console trainer. Hotkeys are global, so they work while the game window has focus.
// Start the game (or the trainer) first; the trainer waits for the game and re-attaches if it restarts.

const int TickMs = 20;
const int DrawEveryMs = 250;
const int ResetKey = 0x7B; // F12
int[] featureKeys = [0x70, 0x71, 0x72, 0x73]; // F1..F4

var trainer = new Trainer();
var down = new bool[featureKeys.Length + 1];
GameProcess? current = null;

// Put the game back to its defaults when the trainer is closed (Ctrl+C or window close).
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    RestoreAndExit();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreDefaults();

Console.CursorVisible = false;
while (true)
{
    Console.Clear();
    Console.WriteLine("Waiting for MetroExodus.exe ...");
    using var game = WaitForGame();
    current = game;
    var build = BuildInfo.FromExe(game.ExePath);
    bool buildOk = build.FileVersion == Offsets.KnownFileVersion;

    long lastDraw = 0;
    while (!game.HasExited)
    {
        bool reset = false;
        for (int i = 0; i < down.Length; i++)
        {
            int key = i < featureKeys.Length ? featureKeys[i] : ResetKey;
            bool pressed = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (pressed && !down[i] && buildOk)
            {
                if (i < featureKeys.Length && i < trainer.Features.Count)
                    trainer.Features[i].Toggle();
                else if (i == featureKeys.Length)
                    reset = true;
            }
            down[i] = pressed;
        }

        if (reset)
            trainer.ResetAll(game);
        else if (buildOk)
            trainer.Tick(game);

        long now = Environment.TickCount64;
        if (now - lastDraw >= DrawEveryMs)
        {
            lastDraw = now;
            Draw(game, build, buildOk, trainer);
        }
        Thread.Sleep(TickMs);
    }

    // Game closed: nothing left to restore, just forget the feature state.
    foreach (var f in trainer.Features) f.Set(false);
    current = null;
}

void RestoreDefaults()
{
    var game = current;
    if (game is null || game.HasExited)
        return;
    try { trainer.ResetAll(game); } catch { /* process went away while shutting down */ }
}

void RestoreAndExit()
{
    RestoreDefaults();
    Environment.Exit(0);
}

static GameProcess WaitForGame()
{
    while (true)
    {
        var game = GameProcess.Attach();
        if (game is not null)
            return game;
        Thread.Sleep(1000);
    }
}

static void Draw(GameProcess game, BuildInfo build, bool buildOk, Trainer trainer)
{
    Console.SetCursorPosition(0, 0);
    var lines = new List<string>
    {
        "MetroExodusTrainer",
        $"Game build: {build}",
        buildOk ? "" : $"UNSUPPORTED BUILD (expected {Offsets.KnownFileVersion}); features are disabled.",
        trainer.PlayerFound ? "Player: found" : "Player: not found (load a save; not in cutscene/menu)",
        "",
    };
    for (int i = 0; i < trainer.Features.Count; i++)
        lines.Add($"  F{i + 1}   [{(trainer.Features[i].Enabled ? "ON " : "off")}]  {trainer.Features[i].Name}");
    lines.Add("  F12        Reset everything to default values");
    lines.Add("");
    lines.Add(Diagnostics(game, trainer));
    lines.Add("");
    lines.Add("Ctrl+C to quit (restores defaults).");

    int width = Math.Max(Console.WindowWidth - 1, 40);
    foreach (var line in lines)
        Console.WriteLine(line.PadRight(width));
}

static string Diagnostics(GameProcess game, Trainer trainer)
{
    string speed = game.TryRead<float>(game.Base + Offsets.PlayerSpeedScale, out var sp) ? sp.ToString("F2") : "-";
    if (!trainer.PlayerFound)
        return $"live: speed_scale={speed}";

    nint p = trainer.Player;
    string F(nint off) => game.TryRead<float>(p + off, out var v) ? v.ToString("F2") : "-";
    string B(nint off) => game.TryRead<byte>(p + off, out var v) ? v.ToString() : "-";
    return $"live: health={F(Offsets.Player.Health)} stamina={F(Offsets.Player.Stamina)} " +
           $"freeze={B(Offsets.Player.StaminaFreeze)} can_use={B(Offsets.Player.CanUseStamina)} " +
           $"state={B(Offsets.Player.StaminaState)} jump={F(Offsets.Player.BaseJump)} speed_scale={speed}";
}

partial class Program
{
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
}
