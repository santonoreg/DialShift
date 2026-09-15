# DialShift

**Your radio, on time.** A native Windows tray radio with a weekly listening schedule.

## Run

Open `artifacts/DialShift-win-x64/DialShift.exe`, or extract the release ZIP and open `DialShift.exe`. Keep the whole folder together: it includes .NET and VLC. No separate runtime or VLC installation is needed. Windows 10/11, x64.

Optional install: right-click `Install.ps1` in the extracted release and choose **Run with PowerShell**. It copies the app to `%LOCALAPPDATA%\Programs\DialShift` and adds a Start menu shortcut, without administrator rights. It does not enable Windows startup unless you already chose that setting.

## Listen

- **Stations → Add station:** name, optional description, direct HTTP/HTTPS audio URL. MP3, AAC and HLS are handled by VLC. Ordinary webpage URLs and playlist files that require selecting a child stream are not supported; use the direct stream URL.
- **Schedule → Add time slot:** choose a station, 24-hour start time and days. Optional show label and enabled toggle. Conflicting enabled slots on the same day/time are rejected.
- Turn on **Follow my schedule** to immediately tune into the latest matching slot, even if that slot began on a previous day.
- Each station continues until the next scheduled start. There are no end-time/stop slots in this version.
- Manual station selection and Pause last until the next scheduled switch. Pause disconnects the live stream; Play rejoins live rather than replaying buffered audio.
- The schedule repeats weekly in the Windows local time zone. Sleep/resume and missed starts catch up to the current slot. It does not wake a sleeping computer. During a repeated daylight-saving hour, a slot fires once per running session; skipped starts catch up after the jump.
- A failed or stalled stream retries, then uses your optional fallback after three failures. While playing a fallback, the original is retried every two minutes. With no fallback, retries continue every 30 seconds after the initial quick retries.
- Closing/minimizing the window keeps the app running in the notification area. Double-click the tray icon to reopen; right-click for playback, stations, volume, schedule toggle and **Quit DialShift**.
- **Settings:** optional launch at Windows sign-in, start in tray, fallback station and local settings folder. Startup is off by default. No playback starts on first launch until you press Play or enable a populated schedule.

## Data

Preferences live at `%LOCALAPPDATA%\DialShift\settings.json`, with atomic writes. An unreadable file is preserved as `settings.json.unreadable-*` before defaults are used. Back up this folder to move stations and schedules. Diagnostic errors go to `dialshift.log` in the same folder. No account, server, analytics or cloud sync. Listening connects directly to each selected radio provider.

## Build and verify

Requires a .NET 10 SDK on Windows. The build script also recognizes a local SDK at `%LOCALAPPDATA%\DialShift\sdk`.

```powershell
./scripts/build.ps1
```

Core checks without external test packages:

```powershell
dotnet run --project DialShift.Tests -c Release
```

Application integration checks (isolated temporary preferences, muted live playback, no startup changes):

```powershell
./artifacts/DialShift-win-x64/DialShift.exe --smoke-test --output C:\temp\dialshift-checks
```

Writes `results.json` and UI renders, then exits. Live-stream checks require network access. `--recovery-test` together with `--smoke-test` additionally checks retry/fallback using an intentionally unavailable local endpoint. Hardware sleep, actual Windows sign-in and audible output require a manual check on the target PC.

## Project

- `DialShift.Core`: station/settings models, local persistence, weekly schedule evaluation and occurrence tracking.
- `DialShift`: WPF interface, native notification icon, LibVLC playback, retry/fallback and Windows startup/resume integration.
- `DialShift.Tests`: deterministic scheduling, persistence and validation checks.

Starter stations use SomaFM's published direct stream links: [Groove Salad](https://somafm.com/groovesalad/directstreamlinks.html), [Drone Zone](https://somafm.com/dronezone/directstreamlinks.html), [Secret Agent](https://somafm.com/secretagent/directstreamlinks.html). Streams can change; edit a station to update its URL. Station names belong to their respective owners; DialShift is unaffiliated.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled dependencies.
