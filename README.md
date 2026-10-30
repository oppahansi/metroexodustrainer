# MetroExodusTrainer

An open-source, single-player trainer for **Metro Exodus Enhanced Edition**. It attaches to the running game,
so nothing is injected permanently and nothing on disk is modified.

Currently supports the **Epic Games Store build 2.0.1.1** only. The trainer refuses to apply anything on a
different build.

## Features

Hotkeys are on the numpad (the game uses the F keys), so **NumLock must be on**. When there are more than nine
features, the next ones use Shift + Num1-Num9.

| Key | Function | Status |
|---|---|---|
| Num1 | Invulnerability | working |
| Num2 | Infinite stamina (no sprint exhaustion) | working |
| Num3 | Fast movement | working |
| Num4 | High jump | working |
| Num5 | Unlimited reserve ammo (reloads work, stacks filled to the carry limit), medkits and throwables | ammo working; medkits and throwables untested |
| Num6 | No filter drain / suffocation | untested |
| Num7 | Infinite magazine (shoot without reloading) | working |
| Num0 | Reset everything to the game's default values | working |

Planned: more consumables (filters, materials, chemicals), no recoil, rapid fire, battery and gas mask features, no clip, and more. See [docs/progress.md](docs/progress.md).

## Running it

Requirements: Windows, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the game.

```
dotnet run --project src/MetroExodusTrainer.Cli
```

Start the game and load a save (hotkeys are global, so they work while the game has focus; NumLock must be on). If the game runs
as administrator, start the trainer as administrator too. Closing the trainer (Ctrl+C) restores the defaults.

## How it works

The game ships its debug symbols (`MetroExodus.pdb`), which gives function names and exact struct layouts.
`tools/` contains scripts that read the symbols and disassemble code, and
[docs/research-notes.md](docs/research-notes.md) lists the findings the trainer is built on. Most features are
plain memory writes. A few need a small, reversible code patch, and these only apply if the original bytes match
exactly.

```
src/MetroExodusTrainer.Core   process access, build check, offsets, features, code patches
src/MetroExodusTrainer.Cli    console front-end (hotkeys, status display)
tools/                        PDB symbol/type dump and disassembly scripts (Python)
docs/                         research notes and progress
```

No game files, symbols or dumps are included in this repository.

## Disclaimer

For single-player, offline use only. Not affiliated with 4A Games, Deep Silver or Epic Games.
Do not use online or to gain an unfair advantage over other players. Use at your own risk and keep a backup of
your saves.

## License

MIT, see [LICENSE](LICENSE).
