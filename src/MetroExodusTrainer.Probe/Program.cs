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
    string[] interesting = ["weapon_", "medkit", "filter", "dagger", "dynamite", "decoy", "molotov", "charger", "gasmask", "torchlight"];
    string last = "";
    while (!game.HasExited)
    {
        var lines = new List<string>();
        nint player = PlayerLocator.Find(game);
        nint inventory = player == 0 ? 0 : game.ReadPointer(player + Offsets.Player.Inventory);
        string flags = game.TryRead<uint>(game.Base + Offsets.PsActorFlags, out var f) ? $"0x{f:X}" : "-";
        if (inventory == 0)
        {
            lines.Add($"actor_flags={flags}  player/inventory not found");
        }
        else
        {
            nint array = game.ReadPointer(inventory + Offsets.Inventory.AllItems);
            game.TryRead<ushort>(inventory + Offsets.Inventory.AllItems + 10, out var count);
            lines.Add($"actor_flags={flags}  items={count}");
            for (int i = 0; i < count && array != 0; i++)
            {
                nint item = game.ReadPointer(array + i * 8);
                if (item == 0)
                    continue;
                nint entity = item - Offsets.Inventory.ItemOffsetInEntity;
                nint vtable = game.ReadPointer(entity);
                string name = ShortName(names.Resolve(vtable - game.Base));
                if (name == "weapon_ammo")
                {
                    nint prm = game.ReadPointer(entity + Offsets.Inventory.EntityStaticParams);
                    string key = prm != 0 && game.TryRead<uint>(prm - Offsets.Inventory.AmmoKeyBias, out var k) ? k.ToString() : "-";
                    string craft = prm != 0 && game.TryRead<byte>(prm - Offsets.Inventory.CraftResBias, out var cr) ? cr.ToString() : "-";
                    string units = game.TryRead<ushort>(entity + Offsets.Inventory.AmmoBoxValue, out var un) ? un.ToString() : "-";
                    lines.Add($"  ammo key={key,-3} craft={craft} units={units}");
                    continue;
                }
                if (!interesting.Any(name.StartsWith))
                    continue;
                string U(nint off) => game.TryRead<uint>(entity + off, out var v) ? v.ToString() : "-";
                string W(nint off) => game.TryRead<ushort>(entity + off, out var v) ? v.ToString() : "-";
                lines.Add($"  {name,-24} loaded={U(Offsets.Inventory.WeaponAmmoLoaded),-6} units={W(Offsets.Inventory.MissileUnits),-4} cnt={U(Offsets.Inventory.MedkitCount)}");
            }
        }

        string text = string.Join(Environment.NewLine, lines);
        if (text != last)
        {
            Console.WriteLine($"--- {DateTime.Now:HH:mm:ss}");
            Console.WriteLine(text);
            last = text;
        }
        Thread.Sleep(100);
    }
    return 0;
}

/// <summary>"uobject_t&lt;object_manager_hud&lt;weapon_ak74,weapon_ak74_hud_item&gt;::object_hud,1,0&gt;::`vftable'" -> "weapon_ak74".</summary>
static string ShortName(string? full)
{
    if (full is null)
        return "?";
    int open = full.LastIndexOf('<', full.IndexOf("::object_hud", StringComparison.Ordinal) is var h and >= 0 ? h : full.Length - 1);
    if (open < 0)
        return full;
    int end = full.IndexOfAny([',', '<'], open + 1);
    return end > open ? full[(open + 1)..end] : full;
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
