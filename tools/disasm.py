"""Disassemble code from a PE by RVA, annotating symbol names from a symbols.tsv dump.

Usage:
  python disasm.py <exe> <symbols.tsv> func <name-substring>      disassemble matching functions
  python disasm.py <exe> <symbols.tsv> rva <hex rva> <hex length>
  python disasm.py <exe> <symbols.tsv> refs <hex displacement>    list functions with an instruction using that displacement
  python disasm.py <exe> <symbols.tsv> callers <hex rva>          list functions with a direct call/jmp to that rva
"""
import bisect
import re
import sys

import capstone
import pefile


def load_symbols(path):
    syms = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 4 and parts[2] in ("5", "10"):
                syms.append((int(parts[0], 16), int(parts[1]), parts[3]))
    syms.sort()
    return syms


class Image:
    def __init__(self, exe):
        self.pe = pefile.PE(exe)
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        self.md.detail = False

    def bytes_at(self, rva, n):
        off = self.pe.get_offset_from_rva(rva)
        return self.pe.__data__[off:off + n]

    def text(self):
        for s in self.pe.sections:
            if s.Name.startswith(b".text"):
                return s.VirtualAddress, s.get_data()
        raise RuntimeError("no .text")


def name_for(syms, keys, rva):
    i = bisect.bisect_right(keys, rva) - 1
    if i >= 0:
        a, size, n = syms[i]
        if rva < a + max(size, 1):
            return n if rva == a else f"{n}+0x{rva - a:x}"
    return None


def disasm(img, syms, keys, rva, length):
    code = img.bytes_at(rva, length)
    for ins in img.md.disasm(code, img.base + rva):
        text = f"{ins.mnemonic} {ins.op_str}"
        m = re.search(r"\[rip ([+-]) (0x[0-9a-f]+)\]", ins.op_str)
        note = ""
        if m:
            target = ins.address + ins.size + (int(m.group(2), 16) * (1 if m.group(1) == "+" else -1)) - img.base
            n = name_for(syms, keys, target)
            note = f"   ; rva 0x{target:x}" + (f" {n}" if n else "")
        elif ins.mnemonic in ("call", "jmp") and ins.op_str.startswith("0x"):
            target = int(ins.op_str, 16) - img.base
            n = name_for(syms, keys, target)
            note = f"   ; {n}" if n else ""
        print(f"  {ins.address - img.base:08x}  {ins.bytes.hex():<24} {text}{note}")


def main():
    exe, symfile, mode, *args = sys.argv[1:]
    img = Image(exe)
    syms = load_symbols(symfile)
    keys = [s[0] for s in syms]

    if mode == "func":
        for rva, size, name in syms:
            if args[0] in name and size:
                print(f"\n{name}  rva=0x{rva:x} size={size}")
                disasm(img, syms, keys, rva, size)
    elif mode == "rva":
        disasm(img, syms, keys, int(args[0], 16), int(args[1], 16))
    elif mode == "callers":
        target = int(args[0], 16)
        text_rva, data = img.text()
        seen = {}
        for opcode in (0xE8, 0xE9):
            pos = data.find(bytes([opcode]))
            while pos != -1 and pos + 5 <= len(data):
                rel = int.from_bytes(data[pos + 1:pos + 5], "little", signed=True)
                if text_rva + pos + 5 + rel == target:
                    rva = text_rva + pos
                    i = bisect.bisect_right(keys, rva) - 1
                    if i >= 0 and rva < syms[i][0] + syms[i][1]:
                        seen.setdefault(syms[i][2], (syms[i][0], rva))
                pos = data.find(bytes([opcode]), pos + 1)
        for name, (start, rva) in sorted(seen.items(), key=lambda kv: kv[1][0]):
            print(f"0x{start:x}  {name}   (call at 0x{rva:x})")
    elif mode == "refs":
        disp = int(args[0], 16)
        needle = re.compile(rb"..", re.S)
        text_rva, data = img.text()
        pat = disp.to_bytes(4, "little")
        seen = {}
        pos = data.find(pat)
        while pos != -1:
            rva = text_rva + pos
            i = bisect.bisect_right(keys, rva) - 1
            if i >= 0 and rva < syms[i][0] + syms[i][1]:
                seen.setdefault(syms[i][2], (syms[i][0], rva))
            pos = data.find(pat, pos + 1)
        for name, (start, rva) in sorted(seen.items(), key=lambda kv: kv[1][0]):
            print(f"0x{start:x}  {name}   (hit at 0x{rva:x})")


if __name__ == "__main__":
    main()
