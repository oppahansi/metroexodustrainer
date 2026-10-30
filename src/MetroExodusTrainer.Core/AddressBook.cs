using System.Reflection.PortableExecutable;

namespace MetroExodusTrainer.Core;

public enum SignatureKind
{
    /// <summary>The address is inside the matched code: match + site offset.</summary>
    Code,

    /// <summary>The address is a global read through a rip-relative operand inside the matched code.</summary>
    Global,
}

/// <param name="Pattern">Space-separated hex bytes; "??" is a wildcard.</param>
/// <param name="SiteOffset">Code: offset of the address inside the pattern.</param>
/// <param name="DispOffset">Global: offset of the 32-bit displacement inside the pattern.</param>
/// <param name="InstrEnd">Global: offset of the end of the accessing instruction (the rip base).</param>
/// <param name="DefaultRva">The value found in the build the signatures were generated from.</param>
public sealed record SignatureEntry(
    string Name, SignatureKind Kind, string Pattern, int SiteOffset, int DispOffset, int InstrEnd, nint DefaultRva);

/// <summary>How well the attached game build is supported.</summary>
public enum BuildSupportLevel
{
    /// <summary>The build the offsets were taken from; every address and vtable table is known.</summary>
    Known,

    /// <summary>Another build: addresses were found by signature scan; vtable-based features are unavailable.</summary>
    Scanned,

    /// <summary>Another build where the essential addresses could not be found.</summary>
    Unsupported,
}

/// <summary>The outcome of resolving the game's addresses for the attached build.</summary>
public sealed record BuildSupport(
    BuildSupportLevel Level, IReadOnlyList<string> Missing, IReadOnlyList<string> Moved, bool VtablesAvailable)
{
    public bool Usable => Level != BuildSupportLevel.Unsupported;

    public bool IsMissing(string name) => Missing.Contains(name);

    public string Summary => Level switch
    {
        BuildSupportLevel.Known => "Known game build.",
        BuildSupportLevel.Scanned => Missing.Count == 0
            ? "Other game build: addresses found by scanning. Weapon, item and enemy features need tables for this build and are off."
            : $"Other game build: {Missing.Count} address(es) not found ({string.Join(", ", Missing)}). Affected features are off.",
        _ => $"Other game build: essential addresses not found ({string.Join(", ", Missing)}).",
    };
}

/// <summary>
/// Resolves the game addresses the trainer uses. The known build uses the stored values (and a scan only cross-checks
/// them); any other build gets its addresses from byte signatures (see tools/gen_signatures.py). Addresses that cannot be
/// found are set to 0 and the features that need them are switched off.
/// </summary>
public static class AddressBook
{
    // Without these the player cannot be located at all.
    private static readonly string[] Essential = ["GLevel", "PlayerVtable"];

    public static BuildSupport Resolve(GameProcess game, BuildInfo build)
    {
        // METROTRAINER_FORCE_SCAN=1 treats the build as unknown, to test the scan path on the known build.
        bool known = build.FileVersion == Offsets.KnownFileVersion &&
                     Environment.GetEnvironmentVariable("METROTRAINER_FORCE_SCAN") != "1";
        RestoreDefaults();
        var scan = SignatureScanner.Scan(game, Generated.GameSignatures.All);

        var moved = new List<string>();
        var missing = new List<string>();

        foreach (var entry in Generated.GameSignatures.All)
        {
            scan.TryGetValue(entry.Name, out var found);
            if (known)
            {
                if (found != entry.DefaultRva)
                    moved.Add(entry.Name); // the stored value is still used; this only flags the disagreement
                continue;
            }

            if (found is nint rva)
                Set(entry.Name, rva);
            else
            {
                Set(entry.Name, 0);
                missing.Add(entry.Name);
            }
        }

        if (known)
            return new BuildSupport(BuildSupportLevel.Known, [], moved, true);

        var level = Essential.Any(missing.Contains) ? BuildSupportLevel.Unsupported : BuildSupportLevel.Scanned;
        return new BuildSupport(level, missing, moved, false);
    }

    /// <summary>Puts the stored values of the known build back (used when the game restarts as a different build).</summary>
    public static void RestoreDefaults()
    {
        foreach (var entry in Generated.GameSignatures.All)
            Set(entry.Name, entry.DefaultRva);
    }

    private static void Set(string name, nint rva)
    {
        switch (name)
        {
            case "GLevel": Offsets.GLevel = rva; break;
            case "GGame": Offsets.World.GGame = rva; break;
            case "GEntities": Offsets.Entities.GEntities = rva; break;
            case "PsActorFlags": Offsets.PsActorFlags = rva; break;
            case "PlayerVtable": Offsets.PlayerVtable = rva; break;
            case "PlayerSpeedScale": Offsets.PlayerSpeedScale = rva; break;
            case "StepConstant": Offsets.Fly.StepConstant = rva; break;
            case "MoveByVelEx": Offsets.Fly.MoveByVelEx = rva; break;
            case "StealthUpdate": Offsets.Stealth.Update = rva; break;
            case "SoundMemoryOnSound": Offsets.Sound.SoundMemoryOnSound = rva; break;
            case "ThreatMemoryOnSound": Offsets.Sound.ThreatMemoryOnSound = rva; break;
            case "SprintAllowedStore": Offsets.SprintAllowedStore = rva; break;
            case "MovementExReset": Offsets.MovementExReset = rva; break;
            case "RecoilCurveEvaluate": Offsets.Weapon.RecoilCurveEvaluate = rva; break;
            case "FireDispersion": Offsets.Weapon.FireDispersion = rva; break;
            case "CalcShotIntervalReturn": Offsets.Weapon.CalcShotIntervalReturn = rva; break;
        }
    }
}

/// <summary>Finds byte signatures in the game's code section.</summary>
public static class SignatureScanner
{
    /// <summary>Resolved RVA per signature name; names that match zero or several places are absent.</summary>
    public static Dictionary<string, nint> Scan(GameProcess game, IEnumerable<SignatureEntry> entries)
    {
        var result = new Dictionary<string, nint>();
        var text = ReadCodeSection(game, out int textRva);
        if (text is null)
            return result;

        foreach (var entry in entries)
        {
            var (bytes, wild) = Parse(entry.Pattern);
            int anchorStart = 0, anchorLength = 0;
            for (int i = 0; i < bytes.Length;)
            {
                if (wild[i]) { i++; continue; }
                int j = i;
                while (j < bytes.Length && !wild[j]) j++;
                if (j - i > anchorLength) (anchorStart, anchorLength) = (i, j - i);
                i = j;
            }
            if (anchorLength < 4)
                continue;

            var anchor = bytes.AsSpan(anchorStart, anchorLength);
            int matches = 0, matchAt = -1, from = 0;
            while (from < text.Length)
            {
                int hit = text.AsSpan(from).IndexOf(anchor);
                if (hit < 0)
                    break;
                int start = from + hit - anchorStart;
                if (start >= 0 && start + bytes.Length <= text.Length && Matches(text, start, bytes, wild))
                {
                    matches++;
                    matchAt = start;
                    if (matches > 1)
                        break;
                }
                from += hit + 1;
            }
            if (matches != 1)
                continue;

            result[entry.Name] = entry.Kind == SignatureKind.Code
                ? textRva + matchAt + entry.SiteOffset
                : textRva + matchAt + entry.InstrEnd + BitConverter.ToInt32(text, matchAt + entry.DispOffset);
        }
        return result;
    }

    private static bool Matches(byte[] text, int start, byte[] pattern, bool[] wild)
    {
        for (int i = 0; i < pattern.Length; i++)
            if (!wild[i] && text[start + i] != pattern[i])
                return false;
        return true;
    }

    private static (byte[] Bytes, bool[] Wild) Parse(string pattern)
    {
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        var wild = new bool[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            wild[i] = parts[i] == "??";
            bytes[i] = wild[i] ? (byte)0 : Convert.ToByte(parts[i], 16);
        }
        return (bytes, wild);
    }

    /// <summary>Reads the executable .text section of the running game (a copy, so patches by this trainer are visible).</summary>
    private static byte[]? ReadCodeSection(GameProcess game, out int rva)
    {
        rva = 0;
        using var stream = File.OpenRead(game.ExePath);
        using var pe = new PEReader(stream);
        var section = pe.PEHeaders.SectionHeaders.FirstOrDefault(s => s.Name == ".text");
        if (section.VirtualSize == 0)
            return null;

        rva = section.VirtualAddress;
        var buffer = new byte[section.VirtualSize];
        const int Chunk = 1 << 20;
        for (int offset = 0; offset < buffer.Length; offset += Chunk)
        {
            int count = Math.Min(Chunk, buffer.Length - offset);
            var part = game.ReadBytes(game.Base + rva + offset, count);
            if (part is null)
                return null;
            part.CopyTo(buffer, offset);
        }
        return buffer;
    }
}
