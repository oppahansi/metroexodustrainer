using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MetroExodusTrainer.Core;

/// <summary>An attached Metro Exodus process with typed memory access.</summary>
public sealed class GameProcess : IDisposable
{
    private readonly nint _handle;

    private GameProcess(Process process, nint handle)
    {
        Process = process;
        _handle = handle;
        Base = process.MainModule!.BaseAddress;
        ExePath = process.MainModule.FileName;
    }

    public Process Process { get; }
    public nint Base { get; }
    public string ExePath { get; }
    public bool HasExited => Process.HasExited;

    /// <summary>Attaches to a running MetroExodus.exe, or returns null if the game is not running.</summary>
    public static GameProcess? Attach()
    {
        var process = Process.GetProcessesByName("MetroExodus").FirstOrDefault();
        if (process is null)
            return null;

        const uint access = NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite |
                            NativeMethods.ProcessVmOperation | NativeMethods.ProcessQueryInformation;
        var handle = NativeMethods.OpenProcess(access, false, process.Id);
        if (handle == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "OpenProcess failed. If the game runs elevated, start the trainer as administrator.");
        return new GameProcess(process, handle);
    }

    public unsafe bool TryRead<T>(nint address, out T value) where T : unmanaged
    {
        T local = default;
        bool ok = NativeMethods.ReadProcessMemory(_handle, address, &local, sizeof(T), out var read) && read == sizeof(T);
        value = ok ? local : default;
        return ok;
    }

    public T Read<T>(nint address) where T : unmanaged =>
        TryRead<T>(address, out var v) ? v : throw new InvalidOperationException($"Read failed at 0x{address:X}");

    public unsafe bool Write<T>(nint address, T value) where T : unmanaged =>
        NativeMethods.WriteProcessMemory(_handle, address, &value, sizeof(T), out var written) && written == sizeof(T);

    /// <summary>Reads the pointer stored at the address; returns 0 on failure.</summary>
    public nint ReadPointer(nint address) => TryRead<nint>(address, out var p) ? p : 0;

    public void Dispose()
    {
        NativeMethods.CloseHandle(_handle);
        Process.Dispose();
    }
}
