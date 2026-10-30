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
| Unlimited crafting resources (stacks the game marks as craft resources, kept at 999) | materials verified in game, chemicals not yet |
| Gas mask never breaks | implemented (mask health), not yet verified |
| Unlimited flashlight & night vision charge | implemented (charger voltage), not yet verified |
| Air weapons stay pressurized | implemented (pressure kept in the high band), not yet verified |
| Fly mode (no gravity, no collisions) | implemented (movement hook), not yet verified |
| Weapons never get dirty | implemented (dirt half-float at weapon +0xB0), not yet verified |
| Low enemy health | works (verified in game) |
| Invisible to enemies | works (verified in game) |
| Enemies cannot hear you | works (verified in game) |
| Freeze time of day | works (verified by reading the game clock) |
| Unlimited medkits and throwables | works (verified in game) |
| No filter drain / suffocation (gas mask) | implemented through the game cheat flags, not yet verified |

## Planned

Most of these need the inventory and weapon structures located first (see open questions in
[research-notes.md](research-notes.md)).

- Consumables: filters; verify throwing knives, hand grenades, decoys and Molotov cocktails
- Gas mask: filter time, play without gas mask
- Enemies and world: freeze enemies (still needs research)

## Infrastructure

- Done: symbol and type dumps from the game's shipped PDB, disassembly helper, process attach, build check,
  checked and reversible code patches and hooks, hotkeys.
- Done: WPF graphical front-end (categories, toggle switches, two columns).
- Next: signature scanning so more game builds (Steam, GOG, older Enhanced builds) can be supported, and
  polish of the graphical front-end.
