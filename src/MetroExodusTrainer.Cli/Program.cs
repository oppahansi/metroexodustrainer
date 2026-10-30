using System.Runtime.InteropServices;
using MetroExodusTrainer.Core;

// Console trainer. Hotkeys are global, so they work while the game window has focus.
// Start the game (or the trainer) first; the trainer waits for the game and re-attaches if it restarts.

const int TickMs = 10;
const int DrawEveryMs = 250;

if (args.Length > 0 && args[0] == "probe")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    { nint pl = g.ReadPointer(g.ReadPointer(g.Base + Offsets.GLevel) + 0x28); Console.WriteLine($"player type={g.Read<byte>(pl + Offsets.Entities.FriendType)}"); }
    foreach (nint npc in Npcs.Find(g))
    {
        g.TryRead<float>(npc + Offsets.Entities.Health, out var hp);
        g.TryRead<byte>(npc + Offsets.Entities.FriendType, out var ft);
        Console.WriteLine($"npc 0x{npc:X} vtbl=0x{g.ReadPointer(npc) - g.Base:X} health={hp} friend={ft} f68=0x{g.Read<uint>(npc + 0x418):X} min25c={g.Read<float>(npc + 0x60C)} imm={g.Read<byte>(npc + 0xA93):X}{g.Read<byte>(npc + 0xA94):X}");
    }
    return;
}

if (args.Length > 0 && args[0] == "watch")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    var last = new Dictionary<nint, float>();
    for (int t = 0; t < 480; t++)
    {
        foreach (nint npc in Npcs.Find(g))
        {
            g.TryRead<float>(npc + Offsets.Entities.Health, out var hp);
            g.TryRead<byte>(npc + Offsets.Entities.FriendType, out var ft);
            if (last.TryGetValue(npc, out var old) && old != hp)
                Console.WriteLine($"{t * 0.25:F2}s npc 0x{npc:X} health {old} -> {hp} friend={ft} vtbl=0x{g.ReadPointer(npc) - g.Base:X}");
            last[npc] = hp;
        }
        Thread.Sleep(250);
    }
    return;
}

if (args.Length > 0 && args[0] == "clock")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    nint dc = g.ReadPointer(g.Base + Offsets.World.GGame) + Offsets.World.DayCycle;
    for (int i = 0; i < 6; i++)
    {
        Console.WriteLine($"start={g.Read<ulong>(dc + Offsets.World.DayStart)} end={g.Read<ulong>(dc + Offsets.World.DayEnd)} speed={g.Read<float>(dc)} game_time={g.Read<ulong>(dc + 0x38)} base_mult={g.Read<float>(dc + 0x58)}");
        Thread.Sleep(1000);
    }
    return;
}

var trainer = new Trainer();
var featureKeys = Enumerable.Range(0, trainer.Features.Count).Select(Hotkeys.ForFeature).ToArray();
var featureDown = new bool[featureKeys.Length];
var featureDebounce = featureKeys.Select(_ => new Hotkeys.Debouncer()).ToArray();
var resetDebounce = new Hotkeys.Debouncer();
bool resetDown = false;
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
        Hotkeys.Update();
        for (int i = 0; i < featureKeys.Length; i++)
        {
            bool pressed = featureDebounce[i].Held(featureKeys[i].IsDown());
            if (pressed && !featureDown[i] && buildOk)
                trainer.Features[i].Toggle();
            featureDown[i] = pressed;
        }

        bool resetPressed = resetDebounce.Held(Hotkeys.Reset.IsDown());
        bool reset = resetPressed && !resetDown && buildOk;
        resetDown = resetPressed;

        if (reset)
            trainer.ResetAll(game);
        else if (buildOk)
            trainer.Tick(game);

        long now = Environment.TickCount64;
        if (now - lastDraw >= DrawEveryMs)
        {
            lastDraw = now;
            Draw(game, build, buildOk, trainer, featureKeys);
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

static void Draw(GameProcess game, BuildInfo build, bool buildOk, Trainer trainer, Hotkey[] featureKeys)
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
    if (!Hotkeys.NumLockOn)
        lines.Add("!! NumLock is OFF - the numpad hotkeys will not work. Press NumLock.");
    for (int i = 0; i < trainer.Features.Count; i++)
        lines.Add($"  {featureKeys[i].Label,-14} [{(trainer.Features[i].Enabled ? "ON " : "off")}]  {trainer.Features[i].Name}" +
                  (trainer.Features[i].Warning is { } w ? $"   !! {w}" : ""));
    lines.Add($"  {Hotkeys.Reset.Label,-14}       Reset everything to default values");
    lines.Add("");
    lines.AddRange(Diagnostics(game, trainer));
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
    return
    [
        $"live: health={F(Offsets.Player.Health)} stamina={F(Offsets.Player.Stamina)} " +
        $"jump={F(Offsets.Player.BaseJump)} (suit default {SuitJump(game, p)}) speed_scale={speed} actor_flags={cheat}",
    ];
}

partial class Program
{
    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint period);
}
