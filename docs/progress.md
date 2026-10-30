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
| Reset to defaults (F12, also on exit) | tested |

## Planned

Most of these need the inventory and weapon structures located first (see open questions in
[research-notes.md](research-notes.md)).

- Ammo and consumables: infinite ammo, no reload, medkits, filters, throwing knives, hand grenades, decoys,
  Molotov cocktails, materials, chemicals
- Gas mask: durability, filter time, play without gas mask
- Batteries and devices: flashlight and night vision charge, "Hellbreath" charge, air bottle pressure
- Weapons: no recoil, maximum accuracy, rapid fire, no overheat, no jam, always clean, no scope sway
- Enemies and world: low enemy health, invisibility, freeze enemies, time of day, lights off, no clip

## Infrastructure

- Done: symbol and type dumps from the game's shipped PDB, disassembly helper, process attach, build check,
  checked and reversible code patches and hooks, hotkeys, state trace recorder.
- Next: signature scanning so more game builds (Steam, GOG, older Enhanced builds) can be supported, and a
  graphical front-end.
