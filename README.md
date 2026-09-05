# RANS0M — Safe Fork

> **Fork** of **[Ixars/ransomdoors](https://github.com/Ixars/ransomdoors)** by **Goplop0959**. Original concept, entity design and base code by **Ixars** — this fork keeps the original spirit but makes it **safe, debugged and standalone**.
> 
> **Doors** by **LSPLASH** — RANSOM/A-90 is their original entity. Unofficial fan recreation, not affiliated.

## What changed vs original

- **No shutdown / no BSOD.** On loss it opens `https://www.youtube.com/watch?v=dQw4w9WgXcQ` (requested `https://www.yout-ube.com/watch?v=dQw4w9WgXcQ` normalized) in default browser (`Global.cs:47` `OpenRickRoll`) — `IntoCriticalProcess`/`shutdown /s /t 0` (`Global.cs:196`) removed.
- **Standalone single-file EXE.** `rans0m.csproj:15` `PublishSingleFile`/`SelfContained`/`win-x64`/`IncludeNativeLibrariesForSelfExtract` — `C:\Ransom_A-90\RansomDoors-Safe.exe` (~70 MB) needs no .NET installed.
- **Konami kill/win:** `Up Up Down Down Left Right Left Right B A Shift` (Shift = Start) via `KonamiCodeDetector.cs:10` — triggers win then **stops the exe** (`Program.cs:37` `Environment.Exit`).
- **First ransom 9-15s** (`Overlay.cs:573`), then 78-600s (`Global.cs:10` `26*3`/`10*60`).
- **Mouse 1cm threshold** (`Overlay.cs:73` `dist>40px` vs old `!=`), single-ransom gate (`Overlay.cs:453` `Interlocked`), face hidden during idle (`Overlay.cs:546`), `Ransomed` top-most fix (`Ransomed.cs:36`, `Overlay.cs:680`).
- **Time left fixed** and **500 coins with 25/30/50/75/100** values (`GoldCoinManager.cs:44` random, `Ransomed.cs:91` deduct).
- **Desktop prank (reversible):** on infection renames Desktop files to `.Ransom`, swaps icons to `file:///C:/Ransom_A-90/Random_A-90.gif` (converted to `ICO`/`BMP` for display, `DesktopRansomManager.cs:160`), changes folders via `desktop.ini`, wallpaper via `SystemParametersInfo`, and for images creates GIF placeholder at original path. All ops per-file `try` and recorded in `C:\Ransom_A-90\restore.json` (`DesktopRansomManager.cs:18`). On win or next launch if `restore.json` exists it auto-restores and deletes JSON. Also tries app window icons for non-critical processes (`EnumWindows`/`WM_SETICON`).
- Bugfixes: thread-safe RNG (`Global.cs:32`), `KeyboardHook` `GetModuleHandle(null)` for single-file (`KeyboardHook.cs:19`), `SoundHelper` MP3 fallback, registry/icon leaks, font leaks, `Opacity` fix, etc.

## Original warning (now safe)

Original really shut down / BSOD'd on failure. This fork **does not** — it rickrolls and reverts Desktop changes via `restore.json`. Still only run on your own machine and close via tray or Konami.

## Requirements

- Windows 10/11 (Win32 hooks, registry, wallpaper)
- [.NET SDK 9.0+](https://dotnet.microsoft.com/) (project targets `net9.0-windows`, `net10.0-windows` also works with .NET 10 SDK) — standalone EXE needs no runtime
- Visual Studio 2022+ optional

## Building & running (no popup, HTTPS with PAT)

```bash
# HTTPS with PAT (no git popup) — replace TOKEN
git clone https://Goplop0959:TOKEN@github.com/Goplop0959/ransomdoors.git
cd ransomdoors
dotnet build -c Release
dotnet run --project rans0m.csproj

# Standalone single-file (no .NET needed on target)
dotnet publish -c Release -r win-x64 --self-contained true
# -> bin/Release/net9.0-windows/win-x64/publish/ransom.exe (~70 MB)
# or publish_final/ransom.exe -> copy to RansomDoors-Safe.exe
```

Or open `rans0m.slnx` in Visual Studio → Build → Publish → Folder, `Self-contained`, `Produce single file`.

App runs from tray (`RANS0M` icon, Close disabled while `underRansom`). `Random_A-90.gif` is auto-created from `ransom_idle` if missing at `C:\Ransom_A-90\Random_A-90.gif`.

## Configuration

`Global.cs:10` `minRansomTime`/`maxRansomTime`, `tauntTitles`/`tauntImages`, `Properties/Resources.resx`.

## SSH

Ed25519 256-bit key generated for this fork:

```
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAINUIBrWIV3Eec4Zs8yYL/Lmp+uL7LMeX95afvfOdLVer Goplop0959@ransomdoors
SHA256:SFvHH23nR8ycKqTuBUWQwEhfUBFWIOw3EeJZLgX0Gpg
```

Public key at `C:\Users\caden\.ssh\id_ed25519.pub`. Add to GitHub `Settings → SSH and GPG keys` if you want SSH remote:

```bash
git remote set-url origin git@github.com:Goplop0959/ransomdoors.git
```

## Credits

- **Original:** [Ixars/ransomdoors](https://github.com/Ixars/ransomdoors) (Ixars)
- **This fork:** Goplop0959 — safe rework, standalone, Konami, file-ransom revert, bugfixes
- **NAudio** for audio, **Doors** by LSPLASH

## License

Same as original `LICENSE.md` — source-available, non-commercial, credit required, no resale.
