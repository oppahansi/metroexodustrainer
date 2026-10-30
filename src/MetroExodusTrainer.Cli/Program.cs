using System.Runtime.InteropServices;
using MetroExodusTrainer.Core;

// Console trainer. Hotkeys are global, so they work while the game window has focus.
// Start the game (or the trainer) first; the trainer waits for the game and re-attaches if it restarts.

const int TickMs = 10;
const int DrawEveryMs = 250;
const int ResetKey = 0x7B; // F12
const int DumpKey = 0x7A;  // F11
int[] featureKeys = [0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76]; // F1..F7

var trainer = new Trainer();
var down = new bool[featureKeys.Length + 2];
var recorder = new TraceRecorder();
string? dumpMessage = null;
GameProcess? current = null;

// Put the game back to its defaults when the trainer is closed (Ctrl+C or window close).
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    RestoreAndExit();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreDefaults();

// Windows sleeps in ~15 ms steps by default; ask for 1 ms so ticks keep up with the game's frame rate.
timeBeginPeriod(1);

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
            int key = i < featureKeys.Length ? featureKeys[i] : i == featureKeys.Length ? ResetKey : DumpKey;
            bool pressed = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (pressed && !down[i] && buildOk)
            {
                if (i < featureKeys.Length && i < trainer.Features.Count)
                    trainer.Features[i].Toggle();
                else if (i == featureKeys.Length)
                    reset = true;
                else
                    dumpMessage = $"Saved trace: {recorder.Dump()}";
            }
            down[i] = pressed;
        }

        if (reset)
            trainer.ResetAll(game);
        else if (buildOk)
            trainer.Tick(game);
        recorder.Sample(game, trainer.Player);

        long now = Environment.TickCount64;
        if (now - lastDraw >= DrawEveryMs)
        {
            lastDraw = now;
            Draw(game, build, buildOk, trainer, dumpMessage);
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

static void Draw(GameProcess game, BuildInfo build, bool buildOk, Trainer trainer, string? dumpMessage)
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
        lines.Add($"  F{i + 1}   [{(trainer.Features[i].Enabled ? "ON " : "off")}]  {trainer.Features[i].Name}" +
                  (trainer.Features[i].Warning is { } w ? $"   !! {w}" : ""));
    lines.Add("  F12        Reset everything to default values");
    lines.Add("  F11        Save the last 60 s of movement values to logs/trace-*.csv");
    lines.Add("");
    lines.AddRange(Diagnostics(game, trainer));
    lines.Add(dumpMessage ?? "");
    lines.Add("Ctrl+C to quit (restores defaults).");

    int width = Math.Max(Console.WindowWidth - 1, 40);
    foreach (var line in lines)
        Console.WriteLine(line.PadRight(width));
}

static string SuitJump(GameProcess game, nint player) =>
    SuitDefaults.TryRead(game, player, out _, out var jump) ? jump.ToString("F2") : "?";

static string[] Diagnostics(GameProcess game, Trainer trainer)
{
    string speed = game.TryRead<float>(game.Base + Offsets.PlayerSpeedScale, out var sp) ? sp.ToString("F2") : "-";
    string cheat = game.TryRead<uint>(game.Base + Offsets.PsActorFlags, out var cf) ? $"0x{cf:X}" : "-";
    if (!trainer.PlayerFound)
        return [$"live: speed_scale={speed} actor_flags={cheat}"];

    nint p = trainer.Player;
    string F(nint off) => game.TryRead<float>(p + off, out var v) ? v.ToString("F2") : "-";
    string B(nint off) => game.TryRead<byte>(p + off, out var v) ? v.ToString() : "-";
    string Quad(nint off) => $"{F(off)}/{F(off + 4)}/{F(off + 8)}/{F(off + 12)}";
    string state = game.TryRead<uint>(p + Offsets.Player.StateReal, out var st) ? $"0x{st:X}" : "-";
    return
    [
        $"live: health={F(Offsets.Player.Health)} stamina={F(Offsets.Player.Stamina)} " +
        $"freeze={B(Offsets.Player.StaminaFreeze)} can_use={B(Offsets.Player.CanUseStamina)} " +
        $"state={B(Offsets.Player.StaminaState)} jump={F(Offsets.Player.BaseJump)} (suit default {SuitJump(game, p)}) speed_scale={speed} actor_flags={cheat}",
        $"move: speed={F(Offsets.Player.MoveSpeed)} flags={state} tired={B(Offsets.Player.TiredForSprint)} " +
        $"damaged={B(Offsets.Player.DamagedForSprint)} blend={F(Offsets.Player.VsSpeedBlend)}",
        $"vs_speed walk/run/sprint/crouch={Quad(Offsets.Player.VsSpeed)}  target={Quad(Offsets.Player.VsSpeedTarget)}  " +
        $"allowed={B(Offsets.Player.VsMoveAllowed)}/{B(Offsets.Player.VsMoveAllowed + 1)}/{B(Offsets.Player.VsMoveAllowed + 2)}/" +
        $"{B(Offsets.Player.VsMoveAllowed + 3)}/{B(Offsets.Player.VsMoveAllowed + 4)}",
    ];
}

partial class Program
{
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint period);
}
