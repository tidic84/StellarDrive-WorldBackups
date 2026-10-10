# World Backups — StellarDrive mod

A MelonLoader mod for **StellarDrive (Demo)** to create, browse, restore and load backups of your worlds.
Packaged for [Stellar Mod Manager](https://github.com/jollyname/StellarModManager).

![thumbnail](Mods/Thumbnail.png)

## Features

- **Backup window** — press **F8** (anywhere) or click the *World Backups* button in the top-right corner of the main menu.
- **Manual backups** with an optional note. In game, *Save + back up* saves the running world first, so the backup holds the latest state.
- **Automatic backup** each time you load one of your worlds (the 10 most recent are kept per world).
- **Load** — restores a backup into its world and launches it straight away (solo, or hosting on Steam if the toggle is on).
- **Restore** — puts the world back to that backup without launching it.
- **New world** — restores a backup as a separate world, so the original stays untouched.
- **Safety backup** — before any restore, the current state of the world is backed up first (5 kept per world), so a restore can always be undone.
- The game's own rolling autosaves (`backup_*.json`, made every 10 minutes) are listed too, and can be restored or deleted.
- Screenshot preview, notes, size, delete, open folder. UI in English and French (follows the system language).

Restore and load only work from the main menu, never while a world is running.

## Where backups live

```
%USERPROFILE%\AppData\LocalLow\CuriousOwlGames\StellarDrive\
├── WorldSaves\<world>\world.json        ← the game's worlds
└── WorldBackups\<world>\<date>\         ← this mod's backups (a full copy of the world folder)
```

Each backup is a plain copy of the world folder plus a `backup-info.json`, so you can also copy one back by hand.

## Settings

`UserData/MelonPreferences.cfg`, section `[WorldBackups]`:

| Key | Default | Meaning |
| --- | --- | --- |
| `ToggleKey` | `F8` | Key that opens and closes the window (a `UnityEngine.KeyCode` name) |
| `AutoBackupOnWorldLoad` | `true` | Make a backup each time a world is loaded |
| `MaxAutoBackupsPerWorld` | `10` | Automatic backups kept per world (0 = all) |
| `SafetyBackupBeforeRestore` | `true` | Back up the world before it is overwritten |
| `MaxSafetyBackupsPerWorld` | `5` | Safety backups kept per world (0 = all) |
| `ShowMainMenuButton` | `true` | Show the button in the main menu |
| `Language` | `auto` | `auto`, `en` or `fr` |
| `UiScale` | `0` | Window scale (0 = based on screen height) |

## Building

Needs the .NET SDK (6+), the game, and MelonLoader 0.7.x installed in the game folder (Stellar Mod Manager installs it for you).

```powershell
./build.ps1                                   # default Steam path
./build.ps1 -GamePath "D:\SteamLibrary\steamapps\common\StellarDrive Demo"
```

This produces `dist/StellarDrive-WorldBackups.zip` in the layout Stellar Mod Manager expects:

```
mod.json
Mods/WorldBackups.dll
```

## Publishing to Stellar Mod Manager

1. Push this folder to a GitHub repo named `StellarDrive-WorldBackups` (branch `main`), keeping `Mods/mod.json` and `Mods/Thumbnail.png`.
2. Create a release tagged `v1.0.0` (must match `version` in `mod.json`) and attach `StellarDrive-WorldBackups.zip` (the asset name must be `<repo>.zip`).
3. Open a PR on [jollyname/StellarModRepository](https://github.com/jollyname/StellarModRepository) adding to `mods.json`:

```json
{ "author": "<your GitHub user>", "repo": "StellarDrive-WorldBackups", "metadata": "Mods" }
```

For a new version: bump `version` in `Mods/mod.json` and in `MelonInfo` (`src/WorldBackupsMod.cs`) and in the csproj, then publish a `v<version>` release with the new zip.
