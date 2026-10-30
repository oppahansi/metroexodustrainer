# MetroExodusTrainer

An open-source, single-player trainer for **Metro Exodus Enhanced Edition**. It attaches to the running game,
so nothing is injected permanently and nothing on disk is modified.

Currently supports the **Epic Games Store build 2.0.1.1** only. The trainer refuses to apply anything on a
different build.

## Features

Hotkeys are on the numpad (the game uses the F keys), so **NumLock must be on**. When there are more than nine
features, the next ones use Ctrl + Num1-Num9, then Alt + Num1-Num9.

| Key | Function | Status |
|---|---|---|
| Num1 | Invulnerability | working |
| Num2 | Infinite stamina (no sprint exhaustion) | working |
| Num3 | Fast movement | working |
| Num4 | High jump | working |
| Num5 | Unlimited reserve ammo (reloads work, stacks filled to the carry limit), medkits and throwables | works |
| Num6 | No filter drain / suffocation | untested |
| Num7 | Unlimited crafting resources (materials and chemicals kept at 999) | materials work |
| Num8 | Infinite magazine (shoot without reloading) | working |
| Num9 | No weapon overheat | working |
| Ctrl+Num1 | No weapon jam | working |
| Ctrl+Num2 | No recoil | working |
| Ctrl+Num3 | Maximum accuracy (no spread) | working |
| Ctrl+Num4 | Rapid fire (half the time between shots) | working |
| Ctrl+Num5 | Weapons never get dirty | untested |
| Ctrl+Num6 | Invisible to enemies (they stop targeting you, and the watch light reads dark) | works |
| Ctrl+Num7 | Low enemy health (hostile NPCs die in one hit) | works |
| Ctrl+Num8 | Enemies cannot hear you | works |
| Ctrl+Num9 | Freeze time of day | works |
| Alt+Num1 | Gas mask never breaks (durability stays full) | untested |
| Alt+Num2 | Unlimited flashlight & night vision charge (hand charger voltage kept full) | untested |
| Alt+Num3 | Air weapons stay pressurized (Tikhar, Hellbreath, Helsing, flamethrower, ...) | untested |
| Alt+Num4 | Fly mode: no gravity, no collisions (W/A/S/D along the look direction, Space up, C down, Left Shift fast; walls still block you) | works |
| Num0 | Reset everything to the game's default values | working |

Planned: filters, battery and gas mask features, no clip, and more. See [docs/progress.md](docs/progress.md).

## Running it

Requirements: Windows, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the game.

```
dotnet run --project src/MetroExodusTrainer.App    # graphical front-end
dotnet run --project src/MetroExodusTrainer.Cli    # console front-end
```

Start the game and load a save (hotkeys are global, so they work while the game has focus; NumLock must be on). If the game runs
as administrator, start the trainer as administrator too. Closing the trainer (Ctrl+C) restores the defaults.

## Antivirus warnings

Antivirus programs (Avast CyberCapture, Windows Defender SmartScreen and others) may flag the trainer's
executable or block it the first time it runs. This is a **false positive**, and it happens for these reasons:

- A trainer does what malware also does: it opens another process (`OpenProcess`), reads and writes its memory
  (`ReadProcessMemory`, `WriteProcessMemory`), changes memory protection (`VirtualProtectEx`) and allocates
  executable memory in it (`VirtualAllocEx`) for the small hooks. Scanners judge behavior, not intent.
- The executable is new and **unsigned**. Reputation-based checks treat any unknown, unsigned program that
  appears for the first time as suspicious until enough other users have run it. Code-signing certificates cost
  money, so this project does not have one.
- Every build produces a different file, so a fresh build has no reputation either.

What you can do: the source is in this repository, so read it and build it yourself with the .NET SDK, then
allow the file in your antivirus (or exclude the build folder). If a scanner flags a build, you can report it
as a false positive to the vendor. Do not download prebuilt copies from other places. Nothing is installed, no
network connections are made, and no game files are changed on disk.

## How it works

The game ships its debug symbols (`MetroExodus.pdb`), which gives function names and exact struct layouts.
`tools/` contains scripts that read the symbols and disassemble code, and
[docs/research-notes.md](docs/research-notes.md) lists the findings the trainer is built on. Most features are
plain memory writes. A few need a small, reversible code patch, and these only apply if the original bytes match
exactly.

```
src/MetroExodusTrainer.Core   process access, build check, offsets, features, code patches
src/MetroExodusTrainer.App    WPF front-end (categories, toggle switches)
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
