using System.Text.Json;
using MetroExodusTrainer.Core.Generated;

namespace MetroExodusTrainer.Core;

/// <summary>
/// Class tables (vtable addresses of weapons, ammo, NPCs, ...) for builds other than the one the trainer ships with.
/// A profile is a JSON file made by <c>tools/gen_weapon_vtables.py</c> from a game build and its PDB. The trainer looks for
/// <c>profiles/*.json</c> next to itself and in the working directory and uses the one whose timestamp matches the game.
/// </summary>
public static class BuildProfiles
{
    private static readonly Dictionary<string, HashSet<long>> Defaults = new()
    {
        ["MagazinedWeaponVtables"] = [.. MagazinedWeaponVtables.Rvas],
        ["AmmoItemVtables"] = [.. AmmoItemVtables.Rvas],
        ["NpcVtables"] = [.. NpcVtables.Rvas],
        ["PneumoWeaponVtables"] = [.. PneumoWeaponVtables.Rvas],
        ["GasmaskVtables"] = [.. GasmaskVtables.Rvas],
        ["ChargerVtables"] = [.. ChargerVtables.Rvas],
        ["NightVisionVtables"] = [.. NightVisionVtables.Rvas],
        ["HellbreathVtables"] = [.. HellbreathVtables.Rvas],
    };

    /// <summary>Puts the tables of the build the trainer ships with back.</summary>
    public static void RestoreDefaults()
    {
        foreach (var (name, set) in Defaults)
            Set(name, [.. set]);
    }

    /// <summary>Loads the profile that matches the build, if there is one. Returns true when the tables were replaced.</summary>
    public static bool TryApply(BuildInfo build)
    {
        string stamp = $"0x{build.TimeDateStamp:X8}";
        foreach (var directory in new[] { Path.Combine(AppContext.BaseDirectory, "profiles"), "profiles" }.Distinct())
        {
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    if (!string.Equals(root.GetProperty("timeDateStamp").GetString(), stamp, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var table in root.GetProperty("vtables").EnumerateObject())
                        Set(table.Name, [.. table.Value.EnumerateArray().Select(v => v.GetInt64())]);
                    return true;
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
                {
                    // An unreadable or foreign file is simply not a matching profile.
                }
            }
        }
        return false;
    }

    private static void Set(string name, HashSet<long> rvas)
    {
        switch (name)
        {
            case "MagazinedWeaponVtables": MagazinedWeaponVtables.Rvas = rvas; break;
            case "AmmoItemVtables": AmmoItemVtables.Rvas = rvas; break;
            case "NpcVtables": NpcVtables.Rvas = rvas; break;
            case "PneumoWeaponVtables": PneumoWeaponVtables.Rvas = rvas; break;
            case "GasmaskVtables": GasmaskVtables.Rvas = rvas; break;
            case "ChargerVtables": ChargerVtables.Rvas = rvas; break;
            case "NightVisionVtables": NightVisionVtables.Rvas = rvas; break;
            case "HellbreathVtables": HellbreathVtables.Rvas = rvas; break;
        }
    }
}
