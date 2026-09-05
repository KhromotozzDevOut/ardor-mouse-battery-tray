# ARDOR Mouse Battery Tray

[Русская версия](README.ru.md) · [Download the latest release](https://github.com/KhromotozzDevOut/ardor-mouse-battery-tray/releases/latest)

A lightweight battery indicator for compatible ARDOR GAMING wireless mice. It displays
the current charge as a clear number in the Windows notification area and as an optional
always-on-top overlay.

This is an unofficial community project and is not affiliated with ARDOR GAMING.

The utility sends only the battery-read command used by the official software. It does not
change DPI, button assignments, onboard memory or other mouse settings. The ARDOR software
may run alongside this indicator, but it is not required.

## Supported mice

| Model | USB VID/PID | Status |
| --- | --- | --- |
| Chimera | `25A7:FA7B/FA7C` | verified on physical hardware |
| Essence | `25A7:FA7B/FA7C` | confirmed from the official software package |
| Phantom / Phantom Wireless | `25A7:FA7B/FA7C` | confirmed from the official software package |
| Prime Wireless | `25A7:FA7B/FA7C` | confirmed from the official software package |
| Ulta | `25A7:FA7B/FA7C` | confirmed from the official software package |
| Immortality PRO Wireless | `25A7:FA7B/FA7C` | confirmed from the official software package |
| Harpy | `3554:F511/F53C` | Nordic/CompX profile implemented; owner verification needed |
| Impact PRO | `3554:F59A/F53C` | Nordic/CompX profile implemented; owner verification needed |
| Phantom PRO V2 | `3554:F52E/F52D` | Nordic/CompX profile implemented; owner verification needed |

Several models share the same hardware ID, so Windows cannot reliably distinguish between
them. The application identifies these devices as `ARDOR CompX` or `ARDOR Nordic/CompX`.
An unknown mouse is never queried merely because its brand or sensor matches.

If your model is missing, [open an issue](https://github.com/KhromotozzDevOut/ardor-mouse-battery-tray/issues/new)
and include its exact name, wired and receiver hardware IDs, and a link to the official
software. See [CONTRIBUTING.md](CONTRIBUTING.md) for the complete checklist.

## Features

- sharp, color-coded battery percentage in the Windows tray;
- optional transparent overlay that stays above regular and borderless full-screen windows;
- four corner positions plus a lockable custom position;
- four widget sizes and four opacity levels;
- low-battery notifications at 20%, 10% and 5%;
- optional per-user Windows startup;
- English and Russian interface with automatic Windows-language detection;
- no telemetry, ads, network access, Python or third-party runtime.

## Language

The application follows the Windows display language by default. Russian Windows uses
Russian; all other system languages currently fall back to English. To override this,
right-click the tray icon and choose **Language → English**, **Russian / Русский**, or
**System default**. The choice is applied immediately and saved for future launches.

## Usage

1. Start `ArdorBatteryTray.exe`.
2. Hover over the colored tray number to see the device and connection state.
3. Double-click the icon to display the current status as a notification.
4. Right-click the icon to refresh, configure the overlay, enable startup, open the ARDOR
   software, change the language, or exit.

### Overlay widget

Enable **Show overlay widget** in the tray menu. You can select a screen corner, size and
opacity. For a custom position, choose **Move widget…**, drag the number, and click
**Pin widget here**. Once pinned, the widget becomes click-through and cannot interfere
with games until move mode is enabled again.

## Installation and distribution

- `ArdorBatteryTray-Setup-v1.2.0.exe` — standard per-user installer; no administrator rights;
- `ArdorBatteryTray-v1.2.0-portable.zip` — portable version without installation;
- `SHA256SUMS.txt` — checksums for published files.

Windows 10 and 11 are supported. The official ARDOR application does not need to be installed
or running. An unsigned installer may trigger Microsoft SmartScreen; removing that warning
from public releases requires a code-signing certificate.

The battery is queried every 30 seconds. If the mouse sleeps, the last verified reading is
kept for five minutes.

## Building

Run `build.ps1` on Windows. It uses the .NET Framework compiler included with Windows and
requires no package download. `release.ps1` creates a portable ZIP, SHA-256 checksums and,
when Inno Setup 6 is available, a per-user installer.

For protocol diagnostics, run `ArdorBatteryTray.exe --probe`. A console build prints one
reading and exits.

The CompX protocol was verified against the installed ARDOR software and a physical Chimera.
IDs for the other models came from their official ARDOR/DNS software packages. The open-source
Mouse Battery Tray implementation was used for additional comparison (MIT, copyright 2026
incconu_two).
