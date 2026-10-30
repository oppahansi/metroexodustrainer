"""Dump all symbols from a PE's PDB via the system dbghelp.dll.

Usage: python dump_symbols.py <path to exe> <output tsv>
The PDB is located next to the exe. Output columns: rva, size, tag, name.
"""
import ctypes
import sys
from ctypes import wintypes

import pefile

SYMOPT_UNDNAME = 0x2
SYMOPT_NO_PROMPTS = 0x80000


class SYMBOL_INFOW(ctypes.Structure):
    _fields_ = [
        ("SizeOfStruct", wintypes.ULONG), ("TypeIndex", wintypes.ULONG),
        ("Reserved", ctypes.c_uint64 * 2), ("Index", wintypes.ULONG),
        ("Size", wintypes.ULONG), ("ModBase", ctypes.c_uint64),
        ("Flags", wintypes.ULONG), ("Value", ctypes.c_uint64),
        ("Address", ctypes.c_uint64), ("Register", wintypes.ULONG),
        ("Scope", wintypes.ULONG), ("Tag", wintypes.ULONG),
        ("NameLen", wintypes.ULONG), ("MaxNameLen", wintypes.ULONG),
        ("Name", wintypes.WCHAR * 1),
    ]


CALLBACK = ctypes.WINFUNCTYPE(wintypes.BOOL, ctypes.POINTER(SYMBOL_INFOW), wintypes.ULONG, ctypes.c_void_p)


def main(exe, out_path):
    pe = pefile.PE(exe, fast_load=True)
    base = pe.OPTIONAL_HEADER.ImageBase
    size = pe.OPTIONAL_HEADER.SizeOfImage

    dbg = ctypes.WinDLL("dbghelp.dll", use_last_error=True)
    dbg.SymLoadModuleExW.restype = ctypes.c_uint64
    dbg.SymLoadModuleExW.argtypes = [wintypes.HANDLE, wintypes.HANDLE, wintypes.LPCWSTR, wintypes.LPCWSTR,
                                     ctypes.c_uint64, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD]
    dbg.SymEnumSymbolsW.argtypes = [wintypes.HANDLE, ctypes.c_uint64, wintypes.LPCWSTR, CALLBACK, ctypes.c_void_p]

    h = wintypes.HANDLE(0x1234)  # arbitrary handle; not a real process
    dbg.SymSetOptions(SYMOPT_UNDNAME | SYMOPT_NO_PROMPTS)
    if not dbg.SymInitializeW(h, None, False):
        raise OSError("SymInitialize failed")
    if not dbg.SymLoadModuleExW(h, None, exe, None, base, size, None, 0):
        raise OSError(f"SymLoadModuleEx failed: {ctypes.get_last_error()}")

    count = 0
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        def cb(info, _sz, _ctx):
            nonlocal count
            s = info.contents
            name = ctypes.wstring_at(ctypes.addressof(s) + SYMBOL_INFOW.Name.offset, s.NameLen).rstrip(chr(0) + " ")
            f.write(f"{s.Address - base:x}\t{s.Size}\t{s.Tag}\t{name}\n")
            count += 1
            return True

        dbg.SymEnumSymbolsW(h, base, "*", CALLBACK(cb), None)
    print(f"{count} symbols -> {out_path}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
