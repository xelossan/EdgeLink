# EdgeLink

Custom cursor transitions between monitors on Windows 11 — independent of how the monitors are
arranged in the system settings.

A link joins a **span of one monitor's edge** with a **span of another's**. The spans may differ in
length and the cursor position is mapped proportionally, so a short span can lead onto the whole
edge of another screen. An edge can carry any number of links.

## Running

Download a build from [Releases](https://github.com/xelossan/EdgeLink/releases):

- `EdgeLink-win-x64.exe` — a single file that needs nothing installed,
- `EdgeLink-win-x64-needs-dotnet10.exe` — a small build for machines with the
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Installing through winget from a self-hosted source is described in
[deploy/winget-source](deploy/winget-source/README.md).

Or build it yourself:

```powershell
dotnet publish src/EdgeLink -c Release -o publish
publish\EdgeLink.exe
```

The application lives in the notification area. Configuration: `%APPDATA%\EdgeLink\config.json`.

## Usage

- **New link:** grab a monitor edge on the schematic and drag it to an edge of another monitor.
- **Range:** drag the handles at the ends, or the whole span; exact pixel values go in the panel on the right.
- **Deleting:** `Delete`, or right-click the span.
- **Pause:** `Ctrl+Alt+Shift+E` or the tray icon menu. Changes apply immediately, so this shortcut
  is also the way out should the cursor get stuck.
- **Block edges outside links:** on a monitor that has at least one working link, the cursor can
  leave only through links. A monitor without links behaves as in plain Windows.
- **Physical sizes:** the schematic draws monitors at their real sizes (from EDID or, without it,
  estimated from Windows scaling). Tablets usually report their size wrongly or not at all, so you
  can click a monitor and enter its diagonal in inches. This affects the drawing only.

## Recognising monitors

Links are attached to a monitor profile in EdgeLink, not to a number or name from Windows.
Whenever the set of screens changes, profiles are matched again:

- ordinary monitors — by EDID serial number and model, and by port,
- virtual displays without EDID (spacedesk and the like) — by resolution, adapter and position,
  because their device path only reflects the order in which they were connected.

A disconnected monitor keeps its links — they wait for it to return. If two indistinguishable
screens get mixed up, click a monitor and use "Swap with…". "Identify monitors" shows the names on the screens.

## Limitations

- Without administrator rights Windows does not deliver mouse movement while a window running as
  administrator is active. The fix: autostart "As administrator".
- Only physical mouse movement is controlled. Movement injected by software (remote desktop, touch
  on a tablet, automation) is passed through unchanged.
- When another program confines the cursor to its window (games), EdgeLink stays out of the way.

## Layout

- `src/EdgeLink.Core` — model, monitor matching and crossing logic; no Windows dependencies.
- `src/EdgeLink` — the WPF application, mouse hook and display enumeration.
- `tests/EdgeLink.Tests` — `dotnet test`.
