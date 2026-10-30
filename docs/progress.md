# Progress

Reference trainer for scope: a 34-function community trainer. Status of each function below.
Tested means confirmed working in the game (Epic build 2.0.1.1).

## Done

| Function | Status |
|---|---|
| Invulnerability | tested |
| Infinite stamina | tested (includes removing the sprint exhaustion slowdown) |
| Fast movement | tested |
| High jump | tested |
| Reset to defaults (Num0, also on exit) | tested |
| Unlimited reserve ammo (reloads work, stacks filled to the real carry limit) | tested |
| Infinite magazine (16 firearms) | tested |
| No overheat, no jam | tested |
| No recoil | tested (code patch) |
| Maximum accuracy | tested (code patch) |
| Rapid fire | tested (hook) |
| Low enemy health | works (verified in game) |
| Invisible to enemies | works (verified in game) |
| Enemies cannot hear you | works (verified in game) |
| Freeze time of day | works (verified by reading the game clock) |
| Unlimited medkits and throwables, no filter drain | implemented through the game's cheat flags, not yet verified |

## Planned

Most of these need the inventory and weapon structures located first (see open questions in
[research-notes.md](research-notes.md)).

- Consumables: filters, materials, chemicals; verify medkits, throwing knives, hand grenades, decoys and
  Molotov cocktails
- Gas mask: durability, filter time, play without gas mask
- Batteries and devices: flashlight and night vision charge, "Hellbreath" charge, air bottle pressure
- Weapons: always clean (dirt), no scope sway
- Enemies and world: freeze enemies, lights off, no clip (still need research)

## Infrastructure

- Done: symbol and type dumps from the game's shipped PDB, disassembly helper, process attach, build check,
  checked and reversible code patches and hooks, hotkeys.
- Next: signature scanning so more game builds (Steam, GOG, older Enhanced builds) can be supported, and a
  graphical front-end.
