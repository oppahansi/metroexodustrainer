using MetroExodusTrainer.Core;

// Read-only probe. Start the game, load a save, then run this. Ctrl+C to quit.
//   (no args)    verify the player pointer chain and show a few values
//   inventory    list the player's inventory items with class names and counters; prints again whenever
//                something changes. Needs research/dumps/symbols.tsv (made by tools/dump_symbols.py) for names.

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

return args.Length > 0 && args[0] == "inventory" ? InventoryDump(game) : PointerChain(game);

static int PointerChain(GameProcess game)
{
    while (!game.HasExited)
    {
        nint player = PlayerLocator.Find(game);
        Console.WriteLine(player == 0
            ? "\nplayer: not found"
            : $"\nplayer=0x{player:X} health={game.Read<float>(player + Offsets.Player.Health):F2} " +
              $"stamina={game.Read<float>(player + Offsets.Player.Stamina):F2} " +
              $"jump={game.Read<float>(player + Offsets.Player.BaseJump):F2}");
        Thread.Sleep(1000);
    }
    return 0;
}

static int InventoryDump(GameProcess game)
{
    var names = LoadVtableNames();
    string last = "";
    while (!game.HasExited)
    {
        var lines = new List<string>();
        nint player = PlayerLocator.Find(game);
        nint inventory = player == 0 ? 0 : game.ReadPointer(player + Offsets.Player.Inventory);
        if (inventory == 0)
        {
            lines.Add("player/inventory not found");
        }
        else
        {
            nint array = game.ReadPointer(inventory + Offsets.Inventory.AllItems);
            game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count);
            lines.Add($"inventory=0x{inventory:X} items={count}");
            for (int i = 0; i < count && array != 0; i++)
            {
                nint item = game.ReadPointer(array + i * 8);
                if (item == 0)
                    continue;
                nint entity = item - Offsets.Inventory.ItemOffsetInEntity;
                nint vtable = game.ReadPointer(entity);
                string name = names.Resolve(vtable - game.Base) ?? $"vtable_rva=0x{vtable - game.Base:X}";
                string F(nint off) => game.TryRead<uint>(entity + off, out var v) ? v.ToString() : "-";
                lines.Add($"  [{i,2}] entity=0x{entity:X} {name,-50} loaded={F(Offsets.Inventory.WeaponAmmoLoaded)} " +
                          $"box={F(Offsets.Inventory.AmmoBoxValue) } medkit_cnt={F(Offsets.Inventory.MedkitCount)} " +
                          $"units={(game.TryRead<ushort>(entity + Offsets.Inventory.MissileUnits, out var u) ? u.ToString() : "-")}");
            }
        }

        string text = string.Join('\n', lines);
        if (text != last)
        {
            Console.WriteLine($"\n--- {DateTime.Now:HH:mm:ss}");
            Console.WriteLine(text);
            last = text;
        }
        Thread.Sleep(250);
    }
    return 0;
}

static VtableNames LoadVtableNames()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "research", "dumps", "symbols.tsv")))
        dir = dir.Parent;
    if (dir is null)
    {
        Console.WriteLine("(research/dumps/symbols.tsv not found; class names unavailable)");
        return new VtableNames([]);
    }
    return VtableNames.Load(Path.Combine(dir.FullName, "research", "dumps", "symbols.tsv"));
}

/// <summary>Maps a vtable RVA to the class name from the symbol dump (data symbols named "...::`vftable'").</summary>
sealed class VtableNames(List<(long Rva, long Size, string Name)> entries)
{
    public static VtableNames Load(string path)
    {
        var list = new List<(long, long, string)>();
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split('\t');
            if (p.Length == 4 && p[2] == "7" && p[3].Contains("vftable"))
                list.Add((Convert.ToInt64(p[0], 16), long.Parse(p[1]), p[3].Trim()));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return new VtableNames(list);
    }

    public string? Resolve(long rva)
    {
        int lo = 0, hi = entries.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (entries[mid].Rva <= rva) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return best >= 0 && rva < entries[best].Rva + Math.Max(entries[best].Size, 8) ? entries[best].Name : null;
    }
}
