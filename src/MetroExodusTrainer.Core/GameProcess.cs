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

    public unsafe byte[]? ReadBytes(nint address, int count)
    {
        var buffer = new byte[count];
        fixed (byte* p = buffer)
        {
            return NativeMethods.ReadProcessMemory(_handle, address, p, count, out var read) && read == count ? buffer : null;
        }
    }

    /// <summary>Writes into executable memory: lifts the page protection, writes, restores it and flushes the CPU cache.</summary>
    public unsafe bool WriteCode(nint address, byte[] bytes)
    {
        if (!NativeMethods.VirtualProtectEx(_handle, address, bytes.Length, NativeMethods.PageExecuteReadWrite, out var old))
            return false;
        bool ok;
        fixed (byte* p = bytes)
        {
            ok = NativeMethods.WriteProcessMemory(_handle, address, p, bytes.Length, out var written) && written == bytes.Length;
        }
        NativeMethods.VirtualProtectEx(_handle, address, bytes.Length, old, out _);
        NativeMethods.FlushInstructionCache(_handle, address, bytes.Length);
        return ok;
    }

    /// <summary>Allocates a read/write/execute block in the game process; 0 on failure. Blocks are never freed.</summary>
    public nint AllocExecutable(int size) =>
        NativeMethods.VirtualAllocEx(_handle, 0, size, NativeMethods.MemCommitReserve, NativeMethods.PageExecuteReadWrite);

    public bool GetBit(nint address, int bit) => (Read<byte>(address) & (1 << bit)) != 0;

    /// <summary>Sets or clears one bit of a byte, leaving the other bits untouched.</summary>
    public bool SetBit(nint address, int bit, bool value)
    {
        if (!TryRead<byte>(address, out var b))
            return false;
        b = value ? (byte)(b | (1 << bit)) : (byte)(b & ~(1 << bit));
        return Write(address, b);
    }

    /// <summary>Reads the pointer stored at the address; returns 0 on failure.</summary>
    public nint ReadPointer(nint address) => TryRead<nint>(address, out var p) ? p : 0;

    public void Dispose()
    {
        NativeMethods.CloseHandle(_handle);
        Process.Dispose();
    }
}
