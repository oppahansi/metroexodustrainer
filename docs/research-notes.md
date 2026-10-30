# Research notes

Target: `MetroExodus.exe` 2.0.1.1 (Epic Games Store build, Enhanced Edition).
Offsets below are RVAs / struct offsets taken from the symbol file the game ships next to the exe.
They are **build-specific**; the trainer must verify the build before using them and should move to
signature scanning for cross-version support.

Regenerate with `tools/dump_symbols.py` and `tools/dump_types.py` (output goes to the git-ignored `research/dumps/`).

## Globals (RVA from image base)
| Symbol | RVA | Notes |
|---|---|---|
| `g_level` | `0x1691c40` | pointer to the current `clevel` |
| `g_game` | `0x1691c08` | pointer to `cgame` |
| `g_player_speed_scale` | `0x163dcc0` | console variable, 48-byte object |

## Locating the player (verified live on 2.0.1.1)
`*(g_level) + 0x28` = `clevel::_control_entity` (`uobject*`) is the player while controlling Artyom.
Its vtable is `uobject_t<player_new,0,25>::vftable` at RVA `0x11E2728`; the trainer uses that to confirm the entity.
`player_new` derives from `cplayer`, so the offsets below apply. Health is normalized (1.0 = full); stamina, speed and jump are 1.0 by default.
Siblings: `_startup_entity` +0x20, `_view_entity` +0x30.

## `cplayer` fields (size 0x3c10)
| Offset | Type | Field | Planned use |
|---|---|---|---|
| `0x03b8` | float | `_health` | invulnerability / health lock |
| `0x0a93` bit 7 | bitfield | `_immortal` | invulnerability |
| `0x0a94` bit 0 | bitfield | `_invulnerable` | invulnerability |
| `0x161a` | bool | `_spend_filters` | infinite filter time |
| `0x161c` | float | `_min_filter_time` | infinite filters |
| `0x2778` | bool | `_vs_stamina_freeze` | infinite stamina |
| `0x27e0` | float | `_base_speed` | fast movement |
| `0x27e4` | float | `_base_jump` | high jump |
| `0x2a1c` | float | `_stamina` | infinite stamina |
| `0x2a21` | bool | `_can_use_stamina` | infinite stamina |

## Functions of interest (RVA)
| Function | RVA |
|---|---|
| `human::notify_ammo_spend` | `0x979380` |
| `weapon_uboynicheg::loaded_ammo_count` | `0x928830` |
| `gasmask::reduce_health` | `0x99ce50` |
| `cplayer::health_restore` | `0x818b40` |
| `cplayer::use_stamina_for_sprint` | `0x818460` |
| `centity::gain_health` | `0x84a520` |
| `vs::action_unlimited_ammo` | (class exists in the engine's script system) |

## Open questions
- Inventory counts (medkits, filters, grenades) are not plain entity fields; trace `inventory_player` / `inventory_item`.
- Weapon state (recoil, spread, overheat, jam, dirt) lives in `weapon_magazined` / `weapon_pneumo`; not yet mapped.

## Movement and stamina findings (verified by disassembly, `tools/disasm.py`)
- `cplayer::_base_speed` (+0x27e0) is only a per-suit setting copied in by `serial_update_suit`; movement code does not read it.
- `cplayer::movement_speed_base` (RVA `0x817e50`) multiplies in the float at RVA `0x163dce8`, the value inside `g_player_speed_scale`. The trainer writes that for fast movement.
- `_vs_speed` (+0x24f0: walk, run, sprint, crouch, jump, accel, brake) holds script-driven speed multipliers (1.0 default).
- `cplayer::update_stamina` (RVA `0x818680`) takes its regeneration path whenever `_vs_stamina_freeze` (+0x2778) is non-zero, so the flag stops sprint drain.

## Stamina and exhaustion (from `use_stamina`, RVA `0x8184f0`)
- `_stamina_state` (+0x2a20): 0 idle, 1 recovering, 2 in use.
- `use_stamina` subtracts the cost, clamps to 0..1, sets state 2, and when stamina drops below the static-params threshold clears `_can_use_stamina` (+0x2a21) and queues `do_restore_stamina_use` (RVA `0x8184c0`), which sets it back to 1.
- `can_sprint` (RVA `0x8d0280`) and `can_jump` require `_can_use_stamina` set and `_stamina` >= the minimum, so exhaustion = `_can_use_stamina == 0`.

## Sprint "exhaustion" is a script, not the stamina value
- `cplayer::_vs_move_allowed` (+0x2528, 5 bytes: walk, run, sprint, crouch, jump) is written by `vs_actor_movement_allowed_set` (RVA `0xa4cd70`); `..._reset` (RVA `0xa4cd20`) sets them all to 1.
- `update_xform` forces each `_vs_speed` multiplier's target to 0 when its allowed byte is 0, and `can_sprint` fails while `_vs_speed.sprint <= 0`.
- Observed live: while "exhausted" stamina=1.0, `can_use`=1, `_vs_speed` sprint=0.0 with target 1.0. The infinite stamina feature therefore also keeps the sprint-allowed byte set.
- The game's default `g_player_speed_scale` is 1.25, not 1.0.

## Sprint slowdown trace (first capture, 2.0.1.1)
- `_move_speed` (+0x14a0) is the *measured* velocity of the physics body (written by `simulate_accel_physic`), not a setting.
- With infinite stamina on, a 14 s sprint held 7.31 then dropped to ~4.9 (x0.667) while stamina, state flags, `_vs_speed`, `_vs_move_allowed` all stayed unchanged.
- Base speed comes from `_vs_speed_mod` (+0x2548 array of 24-byte `{current,target,blend,?,block*}`, u16 count at +0x2552): the top entry, or the static-params float at `[+0x240]+0xe04` when empty. Scripts push entries via `vs_actor_movement_ex_set` (RVA `0xa4cbd0`).

## The sprint "exhaustion" is a timed boost modifier (second capture)
- Sprinting pushes a `_vs_speed_mod` entry whose target ramps 1.0 -> 1.5 (blend 10). About 13 s later, with the sprint flag (`_state_real & 0x400`) still set, the game switches that target back to 1.0; the speed eases down and the entry is popped. 7.31 -> 4.88 equals 1/1.5.
- Stamina, `_can_use_stamina`, `_vs_speed`, and `_vs_move_allowed` are unchanged during it, so stamina is not involved.
- Infinite stamina therefore re-asserts the boost target while sprint is held, and releases it as soon as sprint is released.

## Why external writes could not stop the sprint cut-off (third capture)
- At the moment the boost is lost, `cur_sprint` (+0x24f8) reads 0 for a single sample while its target, the allowed byte, stamina and `can_use` all read normal.
- `update_xform` sets the current multiplier to 0 in the same frame the script clears `_vs_move_allowed[sprint]`, and `process_input` (RVA `0x8d0610`) drops the sprint flag whenever `can_sprint` fails. An external write 10 ms later is too late.
- Infinite stamina therefore patches the store in `vs_actor_movement_allowed_set` (RVA `0xa4cda2`, `88 81 2a 25 00 00` -> six NOPs), only when those exact bytes are present, and reverts it when switched off or on exit.

## The ~13 s boost removal (fourth capture, with the allowed-store patch active)
- The sprint flag and `cur_sprint` stay normal, but at ~13.5 s of sprint the `_vs_speed_mod` count drops 1 -> 0 abruptly (no ramp), and speed falls 7.31 -> 5.0.
- The entry is pushed by `vs::action_actor_movement_ex::on_activate` (-> `vs_actor_movement_ex_set`, RVA `0xa4cbd0`) and removed by `on_deactivate` (-> `vs_actor_movement_ex_reset`, RVA `0xa4c9a0`). The same deactivation also runs on a normal sprint release, so it cannot simply be disabled.
- Infinite stamina re-creates the entry (owner null, current = target = boost) when it vanishes while sprint is still held, and sets its target back to 1.0 once sprint is released so the game pops it.
- `ex_reset` removes entries whose owner is null or matches the caller's block, so a re-created entry is cleaned up by any later reset.

## Why a re-created ownerless boost entry vanished (fifth capture)
- `update_xform` (RVA `0x82fa10`, near `0x82fd39`) removes the top speed-modifier entry when the stack has exactly one entry, its owner (`[entry+0x10]`) is null and `|current - target| < eps`.
- A re-created entry therefore needs the owner of the original boost entry (recorded while the boost was active). On sprint release the trainer sets target = 1.0 and clears the owner so the game ramps it down and removes it.

## Re-creating the entry is impossible; suppress its removal instead (sixth capture)
- Observed warning: "modifier stack not allocated". When the last `_vs_speed_mod` entry is removed the vector releases its storage (`_array` null / capacity 0), so there is nothing to write back into.
- `cplayer::vs_actor_movement_ex_reset` (RVA `0xa4c9a0`) starts with 17 bytes of whole instructions: `40 55 56 41 56 48 83 ec 40 44 0f b7 89 52 25 00 00`. Infinite stamina overwrites them with a 14-byte absolute `jmp` to a stub that returns immediately when `[rcx+0x1d50] & 0x400` (sprint held) and `rdx` equals the recorded boost-block owner, and otherwise runs the displaced instructions and jumps back to `+17`. The hook only installs when those exact bytes are present and is reverted on disable/exit.
- When sprint ends, the trainer sets the surviving boost entry's target to 1.0 and clears its owner so `update_xform` ramps it down and removes it.
