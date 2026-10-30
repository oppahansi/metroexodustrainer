using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace MetroExodusTrainer.Core;

/// <summary>Identifies a game build so build-specific offsets are only used on a matching exe.</summary>
public sealed record BuildInfo(string FileVersion, uint TimeDateStamp, long FileSize)
{
    public static BuildInfo FromExe(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return new BuildInfo(
            FileVersionInfo.GetVersionInfo(path).FileVersion ?? "?",
            (uint)pe.PEHeaders.CoffHeader.TimeDateStamp,
            stream.Length);
    }

    public override string ToString() => $"{FileVersion} (timestamp 0x{TimeDateStamp:X8}, {FileSize} bytes)";
}
