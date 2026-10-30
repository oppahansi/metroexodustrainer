using MetroExodusTrainer.Core;

// Read-only probe: verifies the pointer chain from docs/research-notes.md against a running game.
// Start the game, load a save, then run this. Ctrl+C to quit.

using var game = GameProcess.Attach();
if (game is null)
{
    Console.WriteLine("MetroExodus.exe is not running.");
    return 1;
}

var build = BuildInfo.FromExe(game.ExePath);
Console.WriteLine($"Attached to pid {game.Process.Id}, base 0x{game.Base:X}");
Console.WriteLine($"Build: {build}  (offsets are for {Offsets.KnownFileVersion})");
if (build.FileVersion != Offsets.KnownFileVersion)
    Console.WriteLine("WARNING: build mismatch, values below are probably garbage.");

while (!game.HasExited)
{
    nint level = game.ReadPointer(game.Base + Offsets.GLevel);
    Console.WriteLine($"\ng_level = 0x{level:X}");
    if (level != 0)
    {
        foreach (var (name, off) in new[]
                 {
                     ("startup", Offsets.Level.StartupEntity),
                     ("control", Offsets.Level.ControlEntity),
                     ("view", Offsets.Level.ViewEntity),
                 })
        {
            nint e = game.ReadPointer(level + off);
            nint vtable = e != 0 ? game.ReadPointer(e) : 0;
            long vtableRva = vtable != 0 ? vtable - game.Base : 0;
            string health = e != 0 && game.TryRead<float>(e + Offsets.Player.Health, out var h) ? h.ToString("F1") : "-";
            Console.WriteLine($"  {name,-8} entity=0x{e:X} vtable_rva=0x{vtableRva:X} health={health}");
        }

        nint p = game.ReadPointer(level + Offsets.Level.ControlEntity);
        if (p != 0)
        {
            Console.WriteLine($"  player: immortal={game.Read<byte>(p + Offsets.Player.Immortal)} " +
                              $"invulnerable={game.Read<byte>(p + Offsets.Player.Invulnerable)} " +
                              $"stamina={game.Read<float>(p + Offsets.Player.Stamina):F2} " +
                              $"speed={game.Read<float>(p + Offsets.Player.BaseSpeed):F2} " +
                              $"jump={game.Read<float>(p + Offsets.Player.BaseJump):F2}");
        }
    }
    Thread.Sleep(1000);
}
return 0;
