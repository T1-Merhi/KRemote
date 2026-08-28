# KRemote

Send text or files between your devices on the same local network — PC to PC,
phone to PC, PC to phone.

Open KRemote on both devices, press **Scan**, pick the other device from the list,
then type your text or attach files and press **Send**. It lands in that device's
inbox. Every message can carry an optional **title**.

Files stream in 64 KB chunks with no size limit — a 220 MB file moves in half a
second over a wired LAN, and the app's memory use does not move at all.

There is no server, no account, and no internet involved — the apps talk directly
to each other over your LAN.

---

## Platforms

| Platform | Status | Notes |
| --- | --- | --- |
| Windows 10/11 | Supported | Installed as an MSIX package |
| Android 5.0+ | Supported | Receives only while the app is open — see [Android](#android) |
| iOS | Not yet | The target is written but disabled; enabling it needs a Mac |

---

## Requirements

**To run it:** Windows 10 or 11 (64-bit), or an Android 5.0+ device.

**To build it from source:** the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
plus the MAUI workload:

```bash
dotnet workload install maui
```

Android builds additionally need a JDK 17 and the Android SDK. Installing Visual
Studio 2022 with the *.NET Multi-platform App UI development* workload provides
all of it in one step.

All devices must be on the **same network and subnet** (e.g. all `192.168.1.x`).

---

## Install

### Windows

Build and install the MSIX package:

```bash
dotnet publish -f net9.0-windows10.0.19041.0 -c Release
```

The package declares the **`privateNetworkClientServer`** capability, so Windows
grants it access to the local network at install time. There is no firewall script
to run and no admin prompt for networking.

### Android

```bash
dotnet build -f net9.0-android -c Release -t:Run
```

Or produce an APK with `dotnet publish -f net9.0-android -c Release` and sideload it.

### Run from source

```bash
dotnet build -f net9.0-windows10.0.19041.0
```

---

## Using it

The app has three tabs.

### Inbox

Whatever arrives is appended here, newest first. Unread messages carry a dot and a
bold title; the tab shows an unread count badge.

Select a message to read it. On a wide window the list and the message sit side by
side; on a narrow window or a phone the list fills the screen and selecting a
message opens it with a **Back** button.

| Button | What it does |
| --- | --- |
| **Open** | Opens a received file in whatever it is associated with |
| **Show in folder** | Opens the file's folder with it selected. Windows only |
| **Share** | Hands the file to the system share sheet. Android only |
| **Copy** | Copies the message text — or, for a file, its full path |
| **Save** | Keeps this row so it comes back next time you open KRemote |
| **Delete** | Removes the row from the inbox |

**The inbox is memory-only by default.** Any row you have not pressed **Save** on is
gone when you close the app. Saved rows reload on the next launch.

**Deleting a file's row never deletes the file.** The bytes are already on disk;
the row is just the inbox entry.

Received files never overwrite anything. A second `report.pdf` is saved as
`report (2).pdf`.

### Saved

The same list filtered to saved messages only, with **Unsave** in place of Save.

### New message

Press **New message** on the Inbox tab.

1. Press **Scan** to find devices. Results appear as they are found.
2. Pick a device. If it shows **PIN**, enter its 4-digit PIN and press **Unlock**.
3. Type text, attach files, or both, then press **Send**.

**Title** is optional and applies to both text and files. Whatever you type becomes
the bold line for that message in the receiver's inbox; leave it empty and the inbox
falls back to the first line of the text, or the file's name.

A progress bar shows percent, bytes and the transfer rate while files move.

### Settings

Display name, where received files go (Windows), how several files are sent (one zip
or a group), notification preferences, and PIN protection.

Options that do not apply to the running platform are hidden rather than shown
disabled — Android has no downloads-folder picker, no sound option and no taskbar
flash.

---

## Where things are kept

**Windows** (MSIX apps are redirected to the package's private store):

```
%LocalAppData%\Packages\<package>\LocalCache\Roaming\KRemote\
%UserProfile%\Downloads\KRemote\           received files
```

**Android:**

```
<app data>/settings.json, saved-messages.json
<app data>/Received/                       received files
```

Android received files live in app-private storage. Use **Share** or **Open** to get
them anywhere else — this needs no storage permission and works on every Android
version.

---

## How it works

Both halves live in the same app — every instance listens and can send.

- **Port**: TCP 5555 for messages, UDP 5556 for discovery.
- **Discovery**: two mechanisms run at once. A **UDP broadcast** to port 5556 gets
  near-instant replies from other KRemote apps. Alongside it, a **TCP subnet sweep**
  opens a short connection to port 5555 on all 254 host addresses of each local IPv4
  `/24`, at up to 128 probes in parallel. The sweep is what keeps older KRemote 1.1.0
  desktops discoverable. Results are merged and deduplicated by address. Nothing is
  sent until you press Scan.
- **Messages**: a newline-delimited UTF-8 JSON header over TCP, one exchange per
  connection. The receiver replies `ok`.

```
discover  →  {"type":"discover"}                       (UDP broadcast)
          ←  {"type":"pong","name":"DESKTOP-B"}        (UDP unicast reply)

scan      →  {"type":"ping"}                           (TCP fallback)
          ←  {"type":"pong","name":"DESKTOP-B"}

text      →  {"type":"text","name":"DESKTOP-A","title":"Notes","text":"hello"}
          ←  {"type":"ok"}

file      →  {"type":"file","name":"DESKTOP-A","fileName":"a.pdf","size":41234}
          ←  {"type":"ready"}
          →  <exactly `size` raw bytes>
          ←  {"type":"ok"}
```

Text rides inside the JSON, which escapes newlines and keeps the framing intact, so
line breaks, tabs, quotes, accents and emoji all survive. **File bytes do not** —
they follow the header as a raw stream of known length, read and written 64 KB at a
time, so a multi-gigabyte file never exists in memory on either side.

Three details make that safe rather than merely fast:

- The receiver answers `ready` **before** the first byte moves, so it can refuse a
  transfer without the sender having pushed the whole file first.
- Bytes land in a `.part` file that is renamed only on success. An interrupted
  transfer leaves nothing behind.
- The stall timeout (60 s) is refreshed per chunk. It limits *silence*, not the
  duration of the transfer, so a slow link is never killed for being slow.

### Android

Android suspends network listeners for backgrounded apps, so **a phone receives only
while KRemote is open on screen**. The listener starts when the app resumes and stops
when it is backgrounded. A phone that is not open will not appear in another device's
scan. Sending from the phone always works.

### Security

There is **optional PIN protection and no encryption**. With no PIN, any KRemote
instance that can reach port 5555 can put text in your inbox and write a file into
your received-files folder. With a PIN, senders must present the correct 4 digits
first. Everything crosses the network in plain form either way — use it on a network
you trust, not on public Wi-Fi.

Incoming file names are never trusted. Every directory component is stripped before
the name is used, so `..\..\Windows\System32\evil.dll` is written as `evil.dll`
inside the received-files folder and cannot escape it. Invalid characters are
replaced, Windows reserved names (`CON`, `NUL`, `COM1`…) are prefixed, and nothing is
ever executed — files are only written to disk.

---

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Scan finds nothing | KRemote is not open on the other device, or a firewall is blocking it there |
| Scan finds nothing, firewall is fine | The devices are on different subnets. Check that the first three numbers of their addresses match |
| Phone does not appear in a PC's scan | The phone's app is backgrounded. Android only listens while the app is open |
| `Port 5555 is already in use` | A second copy is already running on this device. Close it. Until you do, this window can send but not receive |
| Guest/public Wi-Fi does not work | Many public networks isolate clients from each other, which blocks all direct device-to-device traffic |
| Where did the file go? | Windows: `%UserProfile%\Downloads\KRemote`. Android: app storage — use **Share** or **Open** |
| I deleted the row but the file is still there | Correct — Delete removes the inbox entry, never the downloaded file |
| A transfer died partway | Nothing is left behind; the partial `.part` file is discarded. Send it again |

---

## Project layout

```
KRemote.sln
KRemote.csproj          MAUI single project, net9.0-android + net9.0-windows
App.xaml(.cs)           Application, resources, listener lifecycle
AppShell.xaml(.cs)      Inbox / Saved / Settings tabs and routes
MauiProgram.cs          Dependency injection wiring
Models/
  Peer.cs               A discovered device
  InboxMessage.cs       A received text or file
  InboxAttachment.cs    One file within a received group
  StagedAttachment.cs   One file queued for sending
  AppSettings.cs        Everything the Settings tab writes
Net/
  Protocol.cs           Ports, frame shapes, chunk size, timeouts
  LineIO.cs             Unbuffered header reads, so file bytes are never swallowed
  PeerServer.cs         Listener: answers probes, receives text and streams files in
  PeerBeacon.cs         UDP discovery responder and broadcaster
  PeerScanner.cs        TCP subnet sweep
  Discovery.cs          Runs beacon and sweep together, deduplicated
  LocalNetwork.cs       Network interface enumeration shared by both
  PeerSender.cs         Sends messages and files to one peer
  PinManager.cs         Per-session unlock state
Platform/
  I*.cs                 Device identity, storage paths, notifier, file actions,
                        folder picker — the platform seams
  *.cs                  Shared implementations, per-platform via #if
Platforms/
  Windows/              WinUI head, MSIX manifest, toast and taskbar flash
  Android/              Activity, application, manifest, notification channel
Services/
  InboxService.cs       The one message collection, plus server lifecycle
  SettingsService.cs    Live settings and persistence
ViewModels/
  MessageListViewModel  Shared behavior behind the Inbox and Saved tabs
  InboxViewModel.cs     Inbox specifics: unread badge, transfer status, save
  SavedViewModel.cs     Saved specifics: filter and unsave
  ShareViewModel.cs     Scan, PIN gate, attachments, send
  SettingsViewModel.cs  Every setting, with per-platform visibility
Views/
  *.xaml(.cs)           One adaptive layout per page, wide and narrow
  AdaptiveLayout.cs     The width rule shared by the two list pages
Storage/
  MessageStore.cs       Reads and writes saved-messages.json
  SettingsStore.cs      Reads and writes settings.json
```

---

## License

MIT — see [LICENSE](LICENSE).
