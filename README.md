# RANS0M — Safe Fork

> **Fork** of **[Ixars/ransomdoors](https://github.com/Ixars/ransomdoors)**. Original concept, entity design and base code by **Ixars** — this fork keeps the original spirit but makes it **safe, debugged and standalone**.
>
> **Doors** by **LSPLASH** — RANSOM/A-90 is their original entity. Unofficial fan recreation, not affiliated.
>
> ## ⚠️ Disclaimer — read before running
>
> This is a **consensual prank program**. It temporarily takes over your
> screen, renames files on your Desktop (reversibly), swaps icons, images,
> taskbar icons and wallpaper (all restored automatically on win, loss
> cleanup, or next launch), plays loud sounds, and opens a link in your
> browser on loss. **Only run it on a computer you own, save your work
> first, close anything you can't afford to have covered, and never run it
> on someone else's machine without their informed consent.** It does not
> steal, upload, encrypt-for-real, or spread anything — every change is
> local and recorded in `restore.json` so it can be undone. See
> `LICENSE.md` for the full terms. You run it at your own risk.

## What changed vs original

- **No shutdown / no BSOD.** On loss it opens `https://www.yout-ube.com/watch?v=dQw4w9WgXcQ` in the default browser (`Global.cs` `OpenRickRoll`) — `IntoCriticalProcess` / `shutdown /s /t 0` removed.
- **Standalone single-file EXE.** `rans0m.csproj` sets `PublishSingleFile` / `SelfContained` / `win-x64` / `IncludeNativeLibrariesForSelfExtract` — `RansomDoors-Safe.exe` (~70 MB) needs no .NET installed. The coin faces, honey pot and face gif are embedded inside it: on launch it unpacks them to a created `%TEMP%\Ransom_A-90` folder (plus derived `.ico` / `.bmp` and `restore.json`). The release contains only the exe — nothing else needs to sit beside it, and no fixed install path is assumed.
- **Konami kill/win:** `Up Up Down Down Left Right Left Right B A Shift` (Shift = Start) via `KonamiCodeDetector.cs` — if a ransom is active it shows the thumbs-up win first, then stops the exe; if idle it just stops the exe (`Program.cs`).
- **First ransom 9-15s** (`Overlay.cs`), then 78-600s (`Global.cs` `minRansomTime` / `maxRansomTime`).
- **Mouse 1cm threshold** (movement under ~40px doesn't trigger), single-ransom gate, face hidden during idle, `Ransomed` top-most fix.
- **Click-only coins, no drag-and-drop:** 8 clickable coin popups spawn on top of the desktop icons. Each shows its `Gold_X.png` face (5 / 10 / 25 / 50 / 75 / 100). A coin popup has a 5% chance to be the `Honey_Pot.png`, which pays the full 500 requirement — overpaid gold is saved as credit and taken off the next ransom.
- **7 taunt popups** kept on screen at once, each replaced by a different one when closed — and they drift around, not just jitter in place.
- **Zero-delay fullscreen sequence:** the attack hits fullscreen with the original attack art the instant movement trips, then the download horror fullscreen, chained back-to-back with a click-through red static + animated pixel-dot border frame over everything (ported from the Python reference sim). Heavy desktop/media/coin work runs in the background *during* the show, never before it. Coin data files are hidden system files — only the clickable popup is ever seen, and the desktop sweep never touches `.gold`.
- **Self-placing coins:** coin popups never spawn under another popup, a coin covered later by a popup is deleted, and replacements regenerate at new free spots until the target count is back.
- **Desktop prank (reversible):** on infection, renames Desktop files to `.Ransom`, swaps file icons (via derived `.ico`), folder icons (via `desktop.ini`), loose images inside Edge / Chrome / Firefox profiles, pinned taskbar shortcut icons, and running app window icons — and animates the wallpaper through the face gif's frames at native resolution. Every op is per-file `try` guarded, strictly bounded (counts/sizes/depths capped for speed), and recorded in `restore.json` in the unpack folder. Winning — or just launching the app again if it was force-stopped — auto-restores everything and deletes the JSON.
- **Audio that finishes:** all players are rooted in a central `AudioEngine` (GC can't cut sounds short), resource bytes are preloaded at startup, and failed plays retry once (channel-per-class design inspired by the Python reference sim).
- Bugfixes + optimizations: thread-safe RNG, single-file-safe `KeyboardHook`, MP3-capable audio, registry/icon leaks, font/GDI leaks, cached screen bounds, consolidated UI timers, background-thread I/O, etc.

## Original warning (now safe)

The original really shut down / BSOD'd on failure. This fork **does not** — it opens the link above and reverts Desktop changes via `restore.json`. Still, only run it on your own machine, and close it via the tray icon or the Konami code.

## Requirements

- Windows 10/11 (Win32 hooks, registry, wallpaper)
- [.NET SDK 9.0+](https://dotnet.microsoft.com/) to build (project targets `net9.0-windows`) — the published EXE needs no runtime
- Visual Studio 2022+ optional

## Building & running

```bash
git clone https://github.com/Goplop0959/ransomdoors.git
cd ransomdoors
dotnet build -c Release
dotnet run --project rans0m.csproj

# Standalone single-file (no .NET needed on target)
dotnet publish -c Release -r win-x64 --self-contained true
# -> a single ransom.exe; rename/copy to RansomDoors-Safe.exe
```

Or open `rans0m.slnx` in Visual Studio → Build → Publish → Folder, `Self-contained`, `Produce single file`.

The app runs from the tray (`RANS0M` icon, Close disabled while a ransom is active). `Random_A-90.gif` ships embedded and is unpacked to `%TEMP%\Ransom_A-90` (falling back to generating it from the embedded face art).

## Configuration

`Global.cs` `minRansomTime` / `maxRansomTime`, `tauntTitles` / `tauntImages`, `Properties/Resources.resx`.

## Credits

- **Original:** [Ixars/ransomdoors](https://github.com/Ixars/ransomdoors) (Ixars)
- Effect design (border frame, red static, rooted audio, restore manifest) inspired by [masashira0212-stack/Doors-Ransom-A-90-Simulation](https://github.com/masashira0212-stack/Doors-Ransom-A-90-Simulation)
- **NAudio** for audio, **Doors** by LSPLASH

## License

Same as original `LICENSE.md` — source-available, non-commercial, credit required, no resale.
