# Research notes

Final findings that the trainer relies on. Target: `MetroExodus.exe` **2.0.1.1**, Epic Games Store build
(Enhanced Edition). All addresses are build-specific; the trainer checks the build and, for every code patch,
the exact original bytes before touching anything.

How these were found: the game ships `MetroExodus.pdb` next to the exe. `tools/dump_symbols.py` and
`tools/dump_types.py` read it (class layouts, field offsets, function addresses), and `tools/disasm.py`
disassembles functions to confirm behavior. Dumps go to the git-ignored `research/dumps/`. No game files or
symbol dumps are committed.

RVA = offset from the image base. Struct offsets are from the object's start.

## Finding the player

| What | Where |
|---|---|
| `g_level` (pointer to the level object) | RVA `0x1691C40` |
| Controlled entity | `*(g_level) + 0x28` (`_control_entity`) |
| Player check | the entity's vtable is `uobject_t<player_new,0,25>::vftable`, RVA `0x11E2728` |

`player_new` derives from `cplayer`, so the `cplayer` offsets below apply. The check also rejects cutscenes,
vehicles and the main menu, where the controlled entity is something else.

## Player fields (`cplayer`)

| Offset | Type | Field | Notes |
|---|---|---|---|
| `0x03B8` | float | `_health` | normalized, 1.0 = full |
| `0x0A93` bit 7 | bit | `_immortal` | |
| `0x0A94` bit 0 | bit | `_invulnerable` | |
| `0x1D50` | uint | `_state_real` | movement state flags; `0x400` = sprint held |
| `0x2528` | 5 bytes | `_vs_move_allowed` | walk, run, sprint, crouch, jump; scripts clear them |
| `0x2548` | vector | `_vs_speed_mod` | script speed modifiers; array +0, capacity u16 +8, size u16 +0xA |
| `0x2764` | struct | `_vs_stamina` | |
| `0x2778` | byte | `_vs_stamina_freeze` | non-zero: `update_stamina` takes its regeneration path |
| `0x27D8` | uint | suit index | selects the entry in the per-suit table |
| `0x27E4` | float | `_base_jump` | live jump multiplier |
| `0x2A1C` | float | `_stamina` | 0..1 |
| `0x2A20` | byte | `_stamina_state` | 0 idle, 1 recovering, 2 in use |
| `0x2A21` | byte | `_can_use_stamina` | cleared by `use_stamina` when stamina falls below a threshold |

Bit-field bytes are only ever modified one bit at a time.

## Features and how they work

### Invulnerability
Sets `_invulnerable` and `_immortal` and tops health back to 1.0 while alive.

### High jump
Writes `_base_jump` (2.25). The real default is read from the per-suit table, never from memory at switch-on
(see Defaults).

### Fast movement
`cplayer::movement_speed_base` (RVA `0x817E50`) multiplies the base speed by the float inside the
`g_player_speed_scale` console variable, at RVA `0x163DCE8` (the variable object is at `0x163DCC0`).
The trainer writes 1.75. The engine default is **1.25**, not 1.0.
`_base_speed` (+0x27E0) is only a per-suit setting that the movement code does not read.

### Infinite stamina (and sprint "exhaustion")
Stamina is not what ends a long sprint. Three separate things have to be handled:

1. **Stamina values.** Set the freeze flag, keep `_stamina` at 1.0 and `_can_use_stamina` at 1.
2. **Sprint-allowed byte.** A script clears `_vs_move_allowed[sprint]` for a frame; `update_xform` then zeroes
   the sprint multiplier and `process_input` drops the sprint flag because `can_sprint` fails. An external write
   cannot win that race, so the store in `vs_actor_movement_allowed_set` is disabled:
   RVA `0xA4CDA2`, `88 81 2A 25 00 00` replaced with six NOPs.
3. **Sprint boost removal.** Sprinting pushes a speed modifier (target 1.5, blend 10) through the script block
   `vs::action_actor_movement_ex` (`on_activate` -> `vs_actor_movement_ex_set`, RVA `0xA4CBD0`). After about
   13 s the block's `on_deactivate` calls `vs_actor_movement_ex_reset` (RVA `0xA4C9A0`) and removes it, which
   drops the speed by a third (7.31 -> ~4.9). The modifier cannot be re-created from outside: the vector frees
   its storage when the last entry is removed, and `update_xform` deletes any single settled entry with no owner.
   Instead a hook at the start of `vs_actor_movement_ex_reset` returns immediately when the sprint flag is held
   and the caller (`rdx`) is the recorded boost block. The first 17 bytes
   (`40 55 56 41 56 48 83 EC 40 44 0F B7 89 52 25 00 00`) are copied into a stub, which falls through to the
   original code in every other case. When sprint ends, the surviving entry's target is set to 1.0 and its owner
   cleared so the game ramps it down and removes it.

Side effect: while this feature is on, scripted "no sprint" sections no longer block sprinting.

### Developer cheat flags (`ps_actor_flags`)
A u32 at RVA `0x15A8824` that game code tests directly, so no code needs patching. Bits used:

| Bit | Mask | Effect (confirmed in the code that tests it) |
|---|---|---|
| 3 | `0x08` | `weapon_magazined::unlimited_ammo` returns true (reloading no longer needs reserve ammo), `medkit::apply_medkit` skips decrementing the medkit count, `missile_weapon::throw_me` skips decrementing the stack |
| 7 | `0x80` | `gasmask::update_state` skips the filter-time check, the suffocation trigger and filter spending |

Other bits seen: bit 0 god mode (`cplayer::can_be_killed`), bit 1 NPC immortality, bit 4 `base_npc::hit`.
Verified in game: bit 3 makes reloading work with zero reserve ammo. It does **not** stop the magazine from
emptying while shooting.

### Inventory
- The player's inventory object is at `player + 0x10F0` (a pointer). Its `_all` vector (`inventory_player + 0x278`,
  array pointer +0, u16 size at +0xA) holds `inventory_item*`.
- Each item pointer is the `inventory_item` subobject; the entity starts at `item - 0x620`. The entity's first
  qword is its vtable, which identifies the class.
- Class recognition uses vtable RVAs generated from the symbols by `tools/gen_weapon_vtables.py`
  (`src/MetroExodusTrainer.Core/Generated/`): firearms that derive from `weapon_magazined`, and `weapon_ammo` items.

### Infinite magazine
For a `weapon_magazined`-derived entity the live loaded-ammo counter is the u32 at `entity + 0x1940`
(`_ammo_loaded`); it drops by one per shot and a reload raises it to the magazine capacity. Keeping each weapon's
counter at the highest value it has shown stops the magazine from emptying. Only objects whose vtable belongs to
a `weapon_magazined` class are ever written to.

### Reserve ammo
- Each ammo type is a `weapon_ammo` item; its reserve rounds are the u16 `units` at `entity + 0x690`
  (`weapon_ammo::units`, `inventory_item + 0x70`).
- The ammo type key is the u32 at `*(entity + 0x240) - 0x2F8`; the byte at `*(entity + 0x240) - 0x2EC` is
  `is_craft_res` (materials and chemicals use the same item class and must be skipped).
- The static data's own `slot_max_num` is a placeholder (99999). The real carry limit is
  `u32 at (*(*(g_level + 0x2A580) + tier * 0x130 + 0x70)) + key * 4`, where `tier = u32 at player + 0x2A40`.
  `0` and `0xFFFFFFFF` mean no usable limit.
- With unlimited ammo on, every non-crafting ammo stack is raised to that limit (never lowered).

### Defaults and reset (Num0)
Values captured when a feature is switched on can be stale if an earlier run was closed with the feature on.
Num0 therefore writes real defaults, whether or not the feature is on:

- Jump: from the per-suit table, `*(g_level + 0x2A4E0) + suit_index * 0x6C + 0x64`
  (count at `g_level + 0x2A4EA`; speed is at `+0x60`). `serial_update_suit` (RVA `0xA4F2B0`) copies these.
- Speed scale: 1.25.
- Invulnerability and immortal bits: cleared. Stamina freeze: 0.
- Code patch and hook are reverted. The same reset runs when the trainer exits.

## Other functions of interest (not used yet)

| Function | RVA |
|---|---|
| `human::notify_ammo_spend` | `0x979380` |
| `weapon_uboynicheg::loaded_ammo_count` | `0x928830` |
| `gasmask::reduce_health` | `0x99CE50` |
| `cplayer::health_restore` | `0x818B40` |
| `centity::gain_health` | `0x84A520` |

## Open questions
- Where the medkit, filter and throwable counts live (unlimited medkits and throwables rely on the cheat flag and are not yet verified in game).
- Weapon state (recoil, spread, overheat, jam, dirt) in `weapon_magazined` / `weapon_pneumo`.
- Signature scanning to support more than build 2.0.1.1 (Steam, GOG, older Enhanced builds).
