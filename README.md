# KRemote

Send a block of text from one PC to another on the same local network.

Open KRemote on both PCs, press **Scan network**, pick the other PC from the list,
type or paste your text, and press **Submit**. The text lands in that PC's inbox.

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

Run **`dist\KRemote-Setup-1.0.0.exe`** and click through it. It:

- installs to `%LocalAppData%\Programs\KRemote` **without an admin prompt**,
- puts a **KRemote shortcut on your desktop** and in the Start Menu,
- offers to add the Windows Firewall rule for port 5555 (this one step asks for
  admin — see [Firewall](#firewall) below),
- registers a proper uninstaller in *Apps & features*.

To set up the second PC, copy that single `KRemote-Setup-1.0.0.exe` onto it (USB
stick, shared folder, whatever) and run it there. It needs nothing preinstalled.

Silent install, if you prefer:

```powershell
.\dist\KRemote-Setup-1.0.0.exe /VERYSILENT /NORESTART /TASKS="desktopicon,firewallrule"
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
Pass `-Version 1.1.0` to stamp a different version, or `-SkipPublish` to reuse
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

### 2. Text to send (top right)

Click a device in the list, then type or paste into the editor. Press **Submit**
(or **Ctrl+Enter**) to send.

The status line under the editor tells you what happened: `Sent to DESKTOP-B at
14:22:07`, or the reason it failed. The editor clears itself only after a
successful send, so nothing is lost if the other PC is unreachable.

One send goes to one selected device.

### 3. Inbox (bottom right)

Text arriving from another PC is appended to the inbox, newest first. Arrival is
**silent** — no popup, no window stealing focus, no sound. Click a message to read
the full text on the right.

Three buttons act on the selected message:

| Button | What it does |
| --- | --- |
| **Copy** | Puts the message text on this PC's clipboard, ready to paste. |
| **Save** | Writes the message to disk so it comes back next time you open KRemote. |
| **Delete** | Removes the message from the inbox, and from disk if it was saved. |

**The inbox is memory-only by default.** Anything you have not pressed **Save** on
is gone when you close the app. Saved messages reload on the next launch and are
marked `saved` in the list.

Saved messages live in:

```
%AppData%\KRemote\saved-messages.json
```

---

## How it works

Both halves live in the same app — every instance listens and can send.

- **Port**: TCP 5555, on every instance.
- **Discovery**: an active subnet scan. Pressing *Scan network* opens a short TCP
  connection to port 5555 on all 254 host addresses of each local IPv4 `/24`, at up
  to 128 probes in parallel, and sends a `ping`. Anything that replies `pong` with
  its machine name is a running KRemote. There are no background broadcasts or
  announcements — nothing is sent until you press the button.
- **Messages**: one UTF-8 JSON object per line over TCP, one message per
  connection. The receiver replies `ok`, which is what turns into the "Sent"
  confirmation on the sender's side.

```
scan   →  {"type":"ping"}
       ←  {"type":"pong","name":"DESKTOP-B"}

send   →  {"type":"text","name":"DESKTOP-A","text":"hello"}
       ←  {"type":"ok"}
```

Because the text is JSON-escaped, line breaks, tabs, quotes, accents and emoji all
survive the trip intact. Large pastes are fine — a 400 KB block was tested.

### Security

There is **no passcode and no encryption**. Any KRemote instance that can reach
port 5555 on your PC can put text in your inbox, and the text crosses the network
in plain form. That is a deliberate trade for zero setup — use it on a network you
trust (your home or office LAN), not on public Wi-Fi.

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
  TextMessage.cs        A received message, with its saved/unsaved state
Net/
  Protocol.cs           Port, frame shapes, JSON helpers
  PeerServer.cs         Listener: answers probes, raises received messages
  PeerScanner.cs        Subnet sweep
  PeerSender.cs         Sends one message to one peer
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
