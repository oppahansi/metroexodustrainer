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
