using System.Runtime.InteropServices;

namespace MetroExodusTrainer.Core;

internal static partial class NativeMethods
{
    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessVmWrite = 0x0020;
    public const uint ProcessVmOperation = 0x0008;
    public const uint ProcessQueryInformation = 0x0400;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ReadProcessMemory(nint process, nint address, void* buffer, nint size, out nint read);

    public const uint PageExecuteReadWrite = 0x40;
    public const uint MemCommitReserve = 0x3000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint VirtualAllocEx(nint process, nint address, nint size, uint allocationType, uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualProtectEx(nint process, nint address, nint size, uint newProtect, out uint oldProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushInstructionCache(nint process, nint address, nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool WriteProcessMemory(nint process, nint address, void* buffer, nint size, out nint written);
}
