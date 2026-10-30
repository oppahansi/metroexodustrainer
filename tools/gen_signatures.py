"""Generate byte signatures that find the trainer's game addresses without symbols.

For every address the trainer needs (functions, hook sites and globals) this picks a short, unique byte pattern from the
game's .text section, with the build-dependent bytes (rip-relative displacements, relative call/jump targets, 64-bit
immediates) turned into wildcards. At run time the trainer scans the game's code for the patterns, so it can find the same
addresses in a different build where the code is unchanged.

  - code:   the pattern covers the address; the address is match + site_offset.
  - global: the pattern covers an instruction that accesses the global through a rip-relative operand; the address is read
            from that operand (match + instr_end + disp32 at match + disp_offset).

Usage: python gen_signatures.py <exe> <symbols.tsv> <output .cs>
Needs symbols.tsv from dump_symbols.py (only to know where functions start and end). Needs capstone, pefile, numpy.
"""
import bisect
import sys

import capstone
import numpy as np

import disasm as D

# name -> (kind, rva). Keep in sync with Offsets.cs.
TARGETS = {
    "GLevel": ("global", 0x1691C40),
    "GGame": ("global", 0x1691C08),
    "GEntities": ("global", 0x1691BF0),
    "PsActorFlags": ("global", 0x15A8824),
    "PlayerVtable": ("global", 0x11E2728),
    "PlayerSpeedScale": ("global", 0x163DCE8),
    "StepConstant": ("global", 0x16FE244),
    "MoveByVelEx": ("code", 0x792020),
    "StealthUpdate": ("code", 0xA90940),
    "SoundMemoryOnSound": ("code", 0xF8CCC0),
    "ThreatMemoryOnSound": ("code", 0xB730C0),
    "SprintAllowedStore": ("code", 0xA4CDA2),
    "MovementExReset": ("code", 0xA4C9A0),
    "RecoilCurveEvaluate": ("code", 0x8F2F90),
    "FireDispersion": ("code", 0xA60650),
    "CalcShotIntervalReturn": ("code", 0x8FC893),
}

LENGTHS = (16, 20, 24, 28, 32, 40, 48, 64, 80, 96)


class Code:
    def __init__(self, img):
        self.img = img
        self.base_rva, self.data = img.text()
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        self.md.detail = True
        self.syms = D.load_symbols(sys.argv[2])
        self.keys = [s[0] for s in self.syms]
        self._cache = {}

    def function_of(self, rva):
        i = bisect.bisect_right(self.keys, rva) - 1
        if i < 0:
            return None
        start, size, name = self.syms[i]
        return (start, size, name) if rva < start + max(size, 1) else None

    def decode(self, start, size):
        """Instructions of a function as (rva, size, bytes, wildcard offsets, rip-rel info)."""
        if start in self._cache:
            return self._cache[start]
        off = start - self.base_rva
        code = bytes(self.data[off:off + size])
        out = []
        for ins in self.md.disasm(code, self.img.base + start):
            wild = set()
            rip = None
            for op in ins.operands:
                if op.type == capstone.x86.X86_OP_MEM and op.mem.base == capstone.x86.X86_REG_RIP:
                    for b in range(ins.disp_size):
                        wild.add(ins.disp_offset + b)
                    rip = (ins.disp_offset, ins.address + ins.size + op.mem.disp - self.img.base)
                elif op.type == capstone.x86.X86_OP_IMM:
                    if ins.group(capstone.x86.X86_GRP_JUMP) or ins.group(capstone.x86.X86_GRP_CALL):
                        if ins.size - ins.imm_offset == 4 and ins.imm_size == 4:  # rel32
                            for b in range(4):
                                wild.add(ins.imm_offset + b)
                    elif ins.imm_size == 8:
                        for b in range(8):
                            wild.add(ins.imm_offset + b)
            out.append((ins.address - self.img.base, ins.size, bytes(ins.bytes), wild, rip))
        self._cache[start] = out
        return out

    def count(self, pattern):
        """Number of matches of a pattern (list of int|None) in .text."""
        anchor_start, anchor_len, best = 0, 0, (0, 0)
        i = 0
        while i < len(pattern):
            if pattern[i] is None:
                i += 1
                continue
            j = i
            while j < len(pattern) and pattern[j] is not None:
                j += 1
            if j - i > best[1]:
                best = (i, j - i)
            i = j
        a, n = best
        if n < 4:
            return 99
        needle = bytes(pattern[a:a + n])
        total = 0
        pos = self.data.find(needle)
        while pos != -1:
            start = pos - a
            if start >= 0 and start + len(pattern) <= len(self.data):
                ok = True
                for k, b in enumerate(pattern):
                    if b is not None and self.data[start + k] != b:
                        ok = False
                        break
                if ok:
                    total += 1
                    if total > 1:
                        return total
            pos = self.data.find(needle, pos + 1)
        return total


def window(instrs, first, last):
    """Pattern (list of int|None) over instrs[first:last+1] and the offset of each instruction in it."""
    pattern, offsets = [], []
    for rva, size, raw, wild, rip in instrs[first:last + 1]:
        offsets.append(len(pattern))
        for k in range(size):
            pattern.append(None if k in wild else raw[k])
    return pattern, offsets


def find_signature(code, rva, want_rip_at=None):
    """Smallest unique window containing the instruction at rva (or the one with the given rip-relative target)."""
    fn = code.function_of(rva)
    if fn is None:
        return None
    instrs = code.decode(fn[0], fn[1])
    idx = next((k for k, ins in enumerate(instrs) if ins[0] == rva), None)
    if idx is None:
        return None
    best = None
    for length in LENGTHS:
        for back in range(0, 7):
            first = idx - back
            if first < 0:
                break
            last = idx
            total = sum(i[1] for i in instrs[first:last + 1])
            while last + 1 < len(instrs) and total < length:
                last += 1
                total += instrs[last][1]
            pattern, offsets = window(instrs, first, last)
            if sum(1 for b in pattern if b is not None) < 12:
                continue
            if code.count(pattern) == 1:
                cand = (len(pattern), pattern, offsets[idx - first], instrs[idx])
                if best is None or cand[0] < best[0]:
                    best = cand
        if best is not None:
            break
    return best


def global_refs(code, targets):
    """For each global rva, the list of instruction rvas that access it rip-relatively."""
    data = code.data
    n = len(data)
    found = {rva: [] for rva in targets}
    for k in range(4):
        arr = np.frombuffer(bytes(data[k:n - (n - k) % 4]), dtype="<i4")
        pos = k + 4 * np.arange(len(arr), dtype=np.int64)
        for rva in targets:
            for tail in range(5):
                want = rva - (code.base_rva + pos + 4 + tail)
                hits = np.nonzero(arr == want)[0]
                for h in hits:
                    found[rva].append(int(code.base_rva + pos[h]))
    result = {}
    for rva, disp_positions in found.items():
        refs = []
        for dp in disp_positions:
            fn = code.function_of(dp)
            if fn is None:
                continue
            for ins in code.decode(fn[0], fn[1]):
                if ins[4] is not None and ins[4][1] == rva and ins[0] + ins[4][0] == dp:
                    refs.append(ins[0])
                    break
        result[rva] = sorted(set(refs))
    return result


def main(exe, symbols, out_path):
    img = D.Image(exe)
    code = Code(img)
    globals_ = {name: rva for name, (kind, rva) in TARGETS.items() if kind == "global"}
    refs = global_refs(code, set(globals_.values()))

    rows = []
    for name, (kind, rva) in TARGETS.items():
        if kind == "code":
            sig = find_signature(code, rva)
            if sig is None:
                print(f"{name}: no unique signature", file=sys.stderr)
                continue
            _, pattern, site, _ = sig
            rows.append((name, "Code", pattern, site, 0, 0, rva))
        else:
            best = None
            for ref in refs[rva][:60]:
                sig = find_signature(code, ref)
                if sig is None:
                    continue
                size, pattern, site, ins = sig
                if best is None or size < best[0]:
                    best = (size, pattern, site, ins)
            if best is None:
                print(f"{name}: no unique reference signature ({len(refs[rva])} references)", file=sys.stderr)
                continue
            size, pattern, site, ins = best
            disp_offset = site + ins[4][0]
            instr_end = site + ins[1]
            rows.append((name, "Global", pattern, site, disp_offset, instr_end, rva))

    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("// <auto-generated> by tools/gen_signatures.py for MetroExodus.exe 2.0.1.1. Do not edit.\n")
        f.write("namespace MetroExodusTrainer.Core.Generated;\n\n")
        f.write("/// <summary>Byte signatures of the addresses the trainer needs; see tools/gen_signatures.py.</summary>\n")
        f.write("public static class GameSignatures\n{\n")
        f.write("    public static readonly SignatureEntry[] All =\n    [\n")
        for name, kind, pattern, site, disp_offset, instr_end, rva in rows:
            text = " ".join("??" if b is None else f"{b:02X}" for b in pattern)
            f.write(f'        new("{name}", SignatureKind.{kind}, "{text}", {site}, {disp_offset}, {instr_end}, 0x{rva:X}),\n')
        f.write("    ];\n}\n")
    for name, kind, pattern, *_ in rows:
        print(f"{name:26} {kind:7} {len(pattern):3} bytes")
    missing = set(TARGETS) - {r[0] for r in rows}
    if missing:
        print("MISSING:", ", ".join(sorted(missing)))


if __name__ == "__main__":
    main(*sys.argv[1:4])
