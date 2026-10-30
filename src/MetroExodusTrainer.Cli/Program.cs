using System.Runtime.InteropServices;
using MetroExodusTrainer.Core;

// Console trainer. Hotkeys are global, so they work while the game window has focus.
// Start the game (or the trainer) first; the trainer waits for the game and re-attaches if it restarts.

const int TickMs = 20;
int[] hotkeys = [0x70, 0x71, 0x72, 0x73]; // F1..F4
var trainer = new Trainer();
var down = new bool[hotkeys.Length];

Console.CursorVisible = false;
while (true)
{
    Console.Clear();
    Console.WriteLine("Waiting for MetroExodus.exe ...");
    using var game = WaitForGame();
    var build = BuildInfo.FromExe(game.ExePath);
    bool buildOk = build.FileVersion == Offsets.KnownFileVersion;

    int lastDrawn = 0;
    while (!game.HasExited)
    {
        for (int i = 0; i < hotkeys.Length && i < trainer.Features.Count; i++)
        {
            bool pressed = (GetAsyncKeyState(hotkeys[i]) & 0x8000) != 0;
            if (pressed && !down[i] && buildOk)
                trainer.Features[i].Toggle();
            down[i] = pressed;
        }

        trainer.Tick(game);

        int hash = HashCode.Combine(trainer.PlayerFound, string.Join(',', trainer.Features.Select(f => f.Enabled)));
        if (hash != lastDrawn)
        {
            lastDrawn = hash;
            Draw(build, buildOk, trainer);
        }
        Thread.Sleep(TickMs);
    }

    // Game closed: forget feature state so nothing is half-applied on the next launch.
    foreach (var f in trainer.Features) f.Set(false);
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

static void Draw(BuildInfo build, bool buildOk, Trainer trainer)
{
    Console.Clear();
    Console.WriteLine("MetroExodusTrainer");
    Console.WriteLine($"Game build: {build}");
    if (!buildOk)
        Console.WriteLine($"UNSUPPORTED BUILD (expected {Offsets.KnownFileVersion}); features are disabled.");
    Console.WriteLine(trainer.PlayerFound ? "Player: found" : "Player: not found (load a save; not in cutscene/menu)");
    Console.WriteLine();
    for (int i = 0; i < trainer.Features.Count; i++)
        Console.WriteLine($"  F{i + 1}  [{(trainer.Features[i].Enabled ? "ON " : "off")}]  {trainer.Features[i].Name}");
    Console.WriteLine();
    Console.WriteLine("Ctrl+C to quit.");
}

partial class Program
{
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
}
