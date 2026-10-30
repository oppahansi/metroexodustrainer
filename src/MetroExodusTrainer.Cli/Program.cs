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

if (args.Length > 2 && args[0] == "peek")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    foreach (var a in args[1..])
    {
        var b = g.ReadBytes(g.Base + (nint)Convert.ToInt64(a, 16), 64);
        Console.WriteLine($"{a}: {(b is null ? "?" : BitConverter.ToString(b))}  float={(b is null ? 0 : BitConverter.ToSingle(b, 0))}");
        if (b is not null)
        {
            nint v = (nint)BitConverter.ToInt64(b, 24);
            var vb = g.ReadBytes(v, 32);
            Console.WriteLine($"   -> value at rva 0x{v - g.Base:X}: {(vb is null ? "?" : BitConverter.ToString(vb))}");
        }
    }
    return;
}

if (args.Length > 0 && args[0] == "sp")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    nint pl = g.ReadPointer(g.ReadPointer(g.Base + Offsets.GLevel) + 0x28);
    nint sp = g.ReadPointer(pl + 0xE0);
    Console.WriteLine($"player=0x{pl:X} sp=0x{sp:X} sp[0]=0x{g.ReadPointer(sp):X} sp[8]=0x{g.Read<long>(sp + 8):X}");
    foreach (nint npc in Npcs.Find(g).Take(3))
    {
        nint s2 = g.ReadPointer(npc + 0xE0);
        Console.WriteLine($"npc=0x{npc:X} sp=0x{s2:X} sp[0]=0x{g.ReadPointer(s2):X}");
    }
    return;
}

if (args.Length > 0 && args[0] == "guns")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    nint pl = g.ReadPointer(g.ReadPointer(g.Base + Offsets.GLevel) + 0x28);
    foreach (nint w in Firearms.Find(g, pl))
    {
        var b = g.ReadBytes(w + 0xB0, 8)!;
        string H(int i) => ((float)BitConverter.ToHalf(b, i)).ToString("F3");
        Console.WriteLine($"gun 0x{w:X} vt=0x{g.ReadPointer(w) - g.Base:X} halves b0={H(0)} b2={H(2)} b4={H(4)} b6={H(6)} ammo={g.Read<uint>(w + Offsets.Inventory.WeaponAmmoLoaded)}");
    }
    return;
}

if (args.Length > 0 && args[0] == "clean")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    nint pl = g.ReadPointer(g.ReadPointer(g.Base + Offsets.GLevel) + 0x28);
    var guns = Firearms.Find(g, pl);
    foreach (nint w in guns) g.Write(w + 0xB0, (ushort)0);
    for (int i = 0; i < 8; i++)
    {
        Thread.Sleep(500);
        Console.WriteLine(string.Join("  ", guns.Select(w => ((float)BitConverter.ToHalf(g.ReadBytes(w + 0xB0, 2)!, 0)).ToString("F3"))));
    }
    return;
}

if (args.Length > 0 && args[0] == "sway")
{
    using var g = GameProcess.Attach() ?? throw new InvalidOperationException("game not running");
    nint pl = g.ReadPointer(g.ReadPointer(g.Base + Offsets.GLevel) + 0x28);
    foreach (nint w in Firearms.Find(g, pl))
    {
        nint prm = g.ReadPointer(w + 0x240);
        Console.WriteLine($"gun 0x{w:X} params=0x{prm:X}");
        nint b = prm - 0x5C;
        for (int o = 0x1200; o < 0x1240; o += 4)
            Console.WriteLine($"   +0x{o:X}: {g.Read<float>(b + o)}  (0x{g.Read<uint>(b + o):X})");
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
