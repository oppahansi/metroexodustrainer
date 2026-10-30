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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool WriteProcessMemory(nint process, nint address, void* buffer, nint size, out nint written);
}
