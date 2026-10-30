"""Print class layouts (members, offsets, base classes) from a PE's PDB via dbghelp.

Usage: python dump_types.py <exe> <class> [<class> ...]
"""
import ctypes
import sys
from ctypes import wintypes

import pefile

SYMOPT_UNDNAME, SYMOPT_NO_PROMPTS = 0x2, 0x80000
TI_SYMTAG, TI_SYMNAME, TI_LENGTH, TI_TYPE, TI_TYPEID, TI_BASETYPE = 0, 1, 2, 3, 4, 5
TI_FINDCHILDREN, TI_OFFSET, TI_COUNT, TI_CHILDRENCOUNT, TI_BITPOSITION = 7, 10, 12, 13, 14
TAG = dict(Data=7, UDT=11, Enum=12, FuncType=13, Pointer=14, Array=15, Base=16, Typedef=17, BaseClass=18)
BASE = {1: "void", 2: "char", 3: "wchar_t", 6: "int", 7: "uint", 8: "float", 10: "bool", 13: "long", 14: "ulong", 31: "bool"}


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


class Pdb:
    def __init__(self, exe):
        pe = pefile.PE(exe, fast_load=True)
        self.base = pe.OPTIONAL_HEADER.ImageBase
        d = self.dbg = ctypes.WinDLL("dbghelp.dll")
        d.SymLoadModuleExW.restype = ctypes.c_uint64
        d.SymLoadModuleExW.argtypes = [wintypes.HANDLE, wintypes.HANDLE, wintypes.LPCWSTR, wintypes.LPCWSTR,
                                       ctypes.c_uint64, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD]
        d.SymGetTypeInfo.argtypes = [wintypes.HANDLE, ctypes.c_uint64, wintypes.ULONG, wintypes.ULONG, ctypes.c_void_p]
        d.SymGetTypeFromNameW.argtypes = [wintypes.HANDLE, ctypes.c_uint64, wintypes.LPCWSTR, ctypes.c_void_p]
        self.h = wintypes.HANDLE(0x1234)
        d.SymSetOptions(SYMOPT_UNDNAME | SYMOPT_NO_PROMPTS)
        d.SymInitializeW(self.h, None, False)
        if not d.SymLoadModuleExW(self.h, None, exe, None, self.base, pe.OPTIONAL_HEADER.SizeOfImage, None, 0):
            raise OSError("SymLoadModuleEx failed")

    def info(self, tid, kind, ctype=wintypes.ULONG):
        v = ctype()
        ok = self.dbg.SymGetTypeInfo(self.h, self.base, tid, kind, ctypes.byref(v))
        return v.value if ok else None

    def name(self, tid):
        p = ctypes.c_void_p()
        if not self.dbg.SymGetTypeInfo(self.h, self.base, tid, TI_SYMNAME, ctypes.byref(p)) or not p.value:
            return None
        s = ctypes.wstring_at(p.value)
        ctypes.windll.kernel32.LocalFree(ctypes.c_void_p(p.value))
        return s

    def children(self, tid):
        n = self.info(tid, TI_CHILDRENCOUNT) or 0
        if not n:
            return []
        buf = (wintypes.ULONG * (n + 2))()
        buf[0] = n
        if not self.dbg.SymGetTypeInfo(self.h, self.base, tid, TI_FINDCHILDREN, buf):
            return []
        return list(buf[2:2 + n])

    def type_str(self, tid, depth=0):
        tag = self.info(tid, TI_SYMTAG)
        if tag == TAG["Base"]:
            return BASE.get(self.info(tid, TI_BASETYPE), f"base{self.info(tid, TI_BASETYPE)}") + \
                   ("" if self.info(tid, TI_LENGTH) in (None, 1, 2, 4, 8) else f"[{self.info(tid, TI_LENGTH)}]")
        sub = self.info(tid, TI_TYPEID)
        if tag == TAG["Pointer"]:
            return (self.type_str(sub, depth + 1) if sub is not None and depth < 4 else "?") + "*"
        if tag == TAG["Array"]:
            return f"{self.type_str(sub, depth + 1)}[{(self.info(tid, TI_LENGTH) or 0) // max(1, self.info(sub, TI_LENGTH) or 1)}]"
        return self.name(tid) or f"<tag{tag}>"

    def find(self, cls):
        buf = ctypes.create_string_buffer(ctypes.sizeof(SYMBOL_INFOW) + 2048)
        si = SYMBOL_INFOW.from_buffer(buf)
        si.SizeOfStruct, si.MaxNameLen = ctypes.sizeof(SYMBOL_INFOW), 1024
        if not self.dbg.SymGetTypeFromNameW(self.h, self.base, cls, buf):
            return None
        return si.TypeIndex

    def layout(self, tid, add=0, out=None):
        out = [] if out is None else out
        for c in self.children(tid):
            tag = self.info(c, TI_SYMTAG)
            off = (self.info(c, TI_OFFSET) or 0) + add
            if tag == TAG["BaseClass"]:
                t = self.info(c, TI_TYPEID)
                out.append((off, 0, f"<base {self.name(t)}>", ""))
                self.layout(t, off, out)
            elif tag == TAG["Data"] and self.info(c, 8) == 7:  # DataIsMember only
                t = self.info(c, TI_TYPEID)
                out.append((off, self.info(t, TI_LENGTH) or 0, self.name(c) or "?", self.type_str(t)))
        return out


def main(exe, classes):
    pdb = Pdb(exe)
    for cls in classes:
        tid = pdb.find(cls)
        if tid is None:
            print(f"== {cls}: not found")
            continue
        print(f"== {cls} (size 0x{pdb.info(tid, TI_LENGTH) or 0:x})")
        for off, size, name, typ in sorted(pdb.layout(tid)):
            print(f"  +0x{off:04x} [{size:>4}] {typ:<28} {name}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
