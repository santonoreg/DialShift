# DialShift for Mac — 0.2.0

Apple Silicon (M-series), macOS 14 Sonoma or newer. The app includes .NET and uses
macOS AVPlayer for audio. No VLC, .NET installation, Homebrew or Rosetta is needed.

## Install and listen

1. Download `DialShift-0.2.0-osx-arm64.zip` from the [v0.2.0 release](https://github.com/tsiger/DialShift/releases/tag/v0.2.0) and double-click it.
2. Drag **DialShift.app** into **Applications**, then open it.
3. This app is ad-hoc signed, not Apple notarized. If macOS blocks
   it, open **System Settings → Privacy & Security → Open Anyway** for DialShift
   after the first launch attempt, then confirm Open. Do not disable Gatekeeper.
4. Choose **Listen** beside a station. Closing the window keeps radio running;
   reopen it from the menu-bar dial icon or Dock. Choose **Quit DialShift** to exit.

The app starts with the same three SomaFM stations as Windows. Your personal
stations are not embedded in the app or its ZIP.

### Updating from an earlier build

Quit DialShift from its menu-bar menu, then replace the old `DialShift.app` with
the new one. Existing stations and schedules stay in Application Support.
Running from the Desktop also works; Applications is recommended for a stable
location when enabling launch at login.

Version 0.2.0 includes the fix for a crash after saving station/schedule edits and other menu refreshes:
the Mac native menu now retains the same root object while its entries update.
It also adds editor error reporting and regression checks for editor validation,
save/edit/delete, persistence, and native menu identity after those operations.

## Bring your Windows stations

Copy `%LOCALAPPDATA%\DialShift\settings.json` from your Windows PC to your Mac.
In DialShift choose **Settings → Import stations & schedule…** (scroll down).
Import replaces stations and schedule after confirmation and preserves a backup
of the previous settings. Launch-at-login preferences stay specific to the Mac.
Enabling/importing an active schedule starts the matching station immediately.

Mac settings and diagnostic logs live in
`~/Library/Application Support/DialShift/`. **Export stations & schedule…** saves
a portable JSON backup.

## Features

- Stations and weekly schedules using the Mac's local time zone.
- Native menu-bar controls, Play/Pause, next station, volume and fallback station.
- Retry/backoff and a return to the requested station after two minutes on fallback.
- Catch-up after sleep; a manual pause lasts until the next scheduled switch.
- Optional launch at login, using a per-user LaunchAgent. Move the app into
  Applications before enabling this. It takes effect at the next login. If you
  later move the app, turn the option off and back on to update its location.
- Direct HTTP/HTTPS MP3, AAC and HLS streams supported by AVPlayer.

Current limits: no track-title metadata on the Mac backend; the station description
is shown instead. VLC-specific formats/options and webpage players are not supported.
The app does not wake a sleeping Mac. Playback and station/schedule editing were
manually confirmed on Apple Silicon with macOS 15.7.3. Hardware sleep/wake and login
still need broader Mac testing. The release is cross-built on Windows, where the
shared UI/playback-flow checks also run. Some cosmetic issues remain.

## Build from source

The Windows WPF project remains separate. `DialShift.Desktop` is the Avalonia Mac
port; both apps share `DialShift.Core`. The Windows preview of the new interface
uses a separate `DialShift-Preview` settings directory.

On Windows with .NET 10 SDK and Python 3:

```powershell
./scripts/build-macos.ps1
```

This publishes `osx-arm64`, assembles the bundle, ad-hoc signs it with a
checksum-verified `rcodesign` tool, verifies native signatures and creates a ZIP
with Unix executable permissions. Build tools stay in the ignored `.tools` folder.
Output stays in ignored `artifacts`. The script does not commit or upload anything.

On macOS you can also run `dotnet run --project DialShift.Desktop` with a .NET 10 SDK.
For a self-contained publish use:

```sh
dotnet publish DialShift.Desktop -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/mac-publish
python3 scripts/package-macos.py --publish artifacts/mac-publish --signer rcodesign
```

The packaging script requires `rcodesign` on PATH or via its `--signer` argument.

Checks:

```powershell
dotnet run --project DialShift.Tests -c Release
dotnet run --project DialShift.Desktop.Tests -c Release
```

On a Mac, the bundled executable also supports an isolated, muted live-stream
smoke check; results and screenshots are written to the folder you name:

```sh
/Applications/DialShift.app/Contents/MacOS/DialShift --smoke-test --output "$HOME/Desktop/DialShift-checks"
```

References: [Avalonia macOS packaging](https://docs.avaloniaui.net/docs/deployment/macos),
[.NET 10 supported platforms](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md),
[Apple AVPlayer](https://developer.apple.com/documentation/avfoundation/avplayer).
