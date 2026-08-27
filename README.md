# KRemote

Send text or a file from one PC to another on the same local network.

Open KRemote on both PCs, press **Scan network**, pick the other PC from the list,
then either type your text and press **Submit**, or press **Send file…** and choose
one. It lands in that PC's inbox. Every message can carry an optional **title**.

Files stream in 64 KB chunks with no size limit — a 220 MB file moves in half a
second over a wired LAN, and the app's memory use does not move at all.

There is no server, no account, and no internet involved — the two apps talk
directly to each other over your LAN.

---

## Requirements

**To install and run it:** Windows 10 or 11, 64-bit. Nothing else — the installer
carries its own copy of .NET, so a bare Windows machine works.

**To build it from source:** the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
Building the *installer* additionally needs Inno Setup 6:

```powershell
winget install --id JRSoftware.InnoSetup --source winget
```

Both PCs must be on the **same network and subnet** (e.g. both `192.168.1.x`).

---

## Install

Two ways in, and you can mix them — installed on one PC, run from source on the
other. They are the same app and talk to each other either way.

### Option A — the installer (recommended)

Run **`dist\KRemote-Setup-1.1.0.exe`** and click through it. It:

- installs to `%LocalAppData%\Programs\KRemote` **without an admin prompt**,
- puts a **KRemote shortcut on your desktop** and in the Start Menu,
- offers to add the Windows Firewall rule for port 5555 (this one step asks for
  admin — see [Firewall](#firewall) below),
- registers a proper uninstaller in *Apps & features*.

To set up the second PC, copy that single `KRemote-Setup-1.1.0.exe` onto it (USB
stick, shared folder, whatever) and run it there. It needs nothing preinstalled.

Silent install, if you prefer:

```powershell
.\dist\KRemote-Setup-1.1.0.exe /VERYSILENT /NORESTART /TASKS="desktopicon,firewallrule"
```

To uninstall: *Settings → Apps → KRemote → Uninstall*, or run
`%LocalAppData%\Programs\KRemote\unins000.exe`. Saved inbox messages are left
behind on purpose; delete `%AppData%\KRemote` if you want them gone too.

### Option B — run from source

```powershell
cd d:\repos\KRemote
dotnet run
```

This needs the .NET 9 SDK and creates no shortcut — it just runs the app.

### Building the installer yourself

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

It publishes the app self-contained into `publish\`, then compiles
`installer\KRemote.iss` into `dist\KRemote-Setup-<version>.exe` (about 43 MB).
Pass `-Version 1.2.0` to stamp a different version, or `-SkipPublish` to reuse
the existing `publish\` output.

### Firewall

KRemote listens on **TCP port 5555**, and Windows blocks that by default. The
installer's *"Allow KRemote through Windows Firewall"* task handles it — it is
ticked by default and asks for one admin confirmation. **This is what makes a PC
findable**; without it, the other PC's scan comes back empty.

If you skipped it, or you are running from source, either accept the Windows
Firewall prompt on first launch (tick **Private networks**), or add the rule
directly:

```powershell
powershell -ExecutionPolicy Bypass -File installer\firewall.ps1 -Action Add -ExePath "$env:LOCALAPPDATA\Programs\KRemote\KRemote.exe"
```

That script elevates itself, and `-Action Remove` takes the rule back out. It logs
what it did to `%TEMP%\KRemote-firewall.log`. Do this on both PCs.

**Checking whether the rule exists needs an elevated shell.** From a normal
PowerShell, `Get-NetFirewallRule` fails with *"Access is denied"* — and with
`-ErrorAction SilentlyContinue` that looks exactly like the rule being absent.
Run this **as administrator**:

```powershell
Get-NetFirewallRule -DisplayName KRemote | Get-NetFirewallPortFilter
```

---

## Using it

The window has three panes.

### 1. Devices (left)

Press **Scan network**. KRemote probes every address in your subnet
(`192.168.1.1` → `192.168.1.254`, for whatever your subnet actually is) and lists
the PCs that answer, by Windows machine name. A sweep takes about one second.

The list is a snapshot, not a live feed — press **Scan network** again after
starting the app on the other PC.

Your own PC never appears in its own list.

### 2. Send (top right)

Click a device in the list, then send one of two things:

- **Text** — type or paste into the editor and press **Submit** (or **Ctrl+Enter**).
- **A file** — press **Send file…**, pick one file, and it starts immediately. A
  progress bar shows percent, bytes and, when it finishes, the transfer rate.

**Title** is optional and applies to both. Whatever you type there becomes the bold
line for that message in the receiver's inbox; leave it empty and the inbox falls
back to the first line of the text, or the file's name.

The status line tells you what happened: `Sent to DESKTOP-B at 14:22:07`, or the
reason it failed. The editor and title clear themselves only after a successful
send, so nothing is lost if the other PC is unreachable.

One send goes to one selected device, one file at a time.

### 3. Inbox (bottom right)

Whatever arrives is appended to the inbox, newest first. Arrival is **silent** — no
popup, no window stealing focus, no sound. The only exception is that an incoming
file shows live progress on the inbox line, because a large transfer would
otherwise look like nothing happening.

Click an item to see it on the right: the full text, or the file's name, size,
sender and path.

| Button | What it does |
| --- | --- |
| **Open** | Opens the received file in whatever it is associated with. Files only. |
| **Show in folder** | Opens Explorer with the file selected. Files only. |
| **Copy** | Copies the message text — or, for a file, its full path. |
| **Save** | Keeps this row so it comes back next time you open KRemote. |
| **Delete** | Removes the row from the inbox. |

**The inbox is memory-only by default.** Any row you have not pressed **Save** on is
gone when you close the app. Saved rows reload on the next launch and are marked
`saved`.

```
%AppData%\KRemote\saved-messages.json     saved inbox rows
%UserProfile%\Downloads\KRemote\          received files
```

**Deleting a file's row never deletes the file.** The bytes are already on disk in
`Downloads\KRemote`; the row is just the inbox entry. Use **Show in folder** if you
want to remove the file itself.

Received files never overwrite anything. A second `report.pdf` is saved as
`report (2).pdf`.

---

## How it works

Both halves live in the same app — every instance listens and can send.

- **Port**: TCP 5555, on every instance.
- **Discovery**: an active subnet scan. Pressing *Scan network* opens a short TCP
  connection to port 5555 on all 254 host addresses of each local IPv4 `/24`, at up
  to 128 probes in parallel, and sends a `ping`. Anything that replies `pong` with
  its machine name is a running KRemote. There are no background broadcasts or
  announcements — nothing is sent until you press the button.
- **Messages**: a newline-delimited UTF-8 JSON header over TCP, one exchange per
  connection. The receiver replies `ok`, which is what turns into the "Sent"
  confirmation on the sender's side.

```
scan   →  {"type":"ping"}
       ←  {"type":"pong","name":"DESKTOP-B"}

text   →  {"type":"text","name":"DESKTOP-A","title":"Notes","text":"hello"}
       ←  {"type":"ok"}

file   →  {"type":"file","name":"DESKTOP-A","fileName":"a.pdf","size":41234}
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
  transfer (unwritable folder, nonsense size) without the sender having pushed the
  whole file first.
- Bytes land in a `.part` file that is renamed only on success. An interrupted
  transfer leaves nothing behind, rather than a truncated file that looks real.
- The stall timeout (60 s) is refreshed per chunk. It limits *silence*, not the
  duration of the transfer, so a slow link is never killed for being slow.

### Security

There is **no passcode and no encryption**. Any KRemote instance that can reach port
5555 on your PC can put text in your inbox **and write a file into
`Downloads\KRemote`**, and everything crosses the network in plain form. That is a
deliberate trade for zero setup — use it on a network you trust (your home or office
LAN), not on public Wi-Fi.

Incoming file names are never trusted. Every directory component is stripped before
the name is used, so `..\..\Windows\System32\evil.dll` is written as `evil.dll`
inside the downloads folder and cannot escape it. Invalid characters are replaced,
Windows reserved names (`CON`, `NUL`, `COM1`…) are prefixed, and nothing is ever
executed — files are only written to disk.

---

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Scan finds nothing | KRemote is not open on the other PC, or the firewall rule is missing there. See [Firewall](#firewall). |
| Scan finds nothing, firewall is fine | The two PCs are on different subnets (e.g. one on Wi-Fi `192.168.1.x`, one on Ethernet `192.168.0.x`). Check with `ipconfig` on both — the first three numbers must match. |
| `Port 5555 is already in use…` in the bottom-left | A second copy of KRemote is already running on this PC. Close it. Until you do, this window can send but not receive. |
| `Could not send…: No connection could be made` | The other app was closed after your last scan. Scan again. |
| Guest/public Wi-Fi does not work | Many public and guest networks isolate clients from each other, which blocks all direct PC-to-PC traffic. Nothing in the app can work around that. |
| Message arrived but I did not notice | By design — arrival is silent. Check the inbox pane. |
| Where did the file go? | `%UserProfile%\Downloads\KRemote`. Select the row and press **Show in folder**. |
| I deleted the row but the file is still there | Correct — Delete removes the inbox entry, never the downloaded file. Delete the file from Explorer. |
| A transfer died partway | Nothing is left behind; the partial `.part` file is discarded. Send it again. |
| File arrived with `(2)` in the name | A file of that name already existed. KRemote never overwrites. |
| Installer says the app is already installed | Run the uninstaller first, or just install over the top — the version is upgraded in place. |
| `Get-NetFirewallRule` says the rule is missing | You are querying from a non-elevated shell, where the call fails with "Access is denied" rather than reporting the truth. Re-run it as administrator. |

---

## Project layout

```
KRemote.sln
KRemote.csproj          WPF app, net9.0-windows
KRemote.ico             App and shortcut icon
App.xaml                Application resources and control styles
MainWindow.xaml(.cs)    The three panes and all UI behavior
Models/
  Peer.cs               A discovered PC: machine name + address
  InboxMessage.cs       A received text or file, with its saved/unsaved state
Net/
  Protocol.cs           Port, frame shapes, chunk size, timeouts
  LineIO.cs             Unbuffered header reads, so file bytes are never swallowed
  PeerServer.cs         Listener: answers probes, receives text and streams files in
  PeerScanner.cs        Subnet sweep
  PeerSender.cs         Sends one message or one file to one peer
Storage/
  MessageStore.cs       Reads and writes saved-messages.json
installer/
  build-installer.ps1   Publish + compile in one command
  KRemote.iss           Inno Setup script
  firewall.ps1          Self-elevating firewall rule add/remove
publish/                Self-contained build output (generated, ignored by git)
dist/                   KRemote-Setup-<version>.exe (generated, ignored by git)
```

---

## License

MIT — see [LICENSE](LICENSE).
