# KRemote

**Send text and files from one PC to another on the same local network.**

Open KRemote on both PCs, press **Share**, pick the other PC, then type a
message, attach files, or both. It lands in that PC's inbox in seconds.

There is no server, no account, no sign-in and no internet involved — the two
apps talk directly to each other over your own network. Nothing you send leaves
the building.

**What it is good for**

- Moving a file to the PC on the other side of the room without a USB stick,
  a cloud upload, or emailing it to yourself.
- Sending a link, a password, a code snippet or a paragraph of notes to another
  machine you are sitting at.
- Any size of file. Transfers stream in the background — a 220 MB file takes
  about half a second on a wired network.

**What it is not**

It only works between PCs on the same local network, and it has no encryption.
It is built for a home or office LAN you trust, not for the internet and not for
public Wi-Fi. See [A note on privacy](#a-note-on-privacy).

---

## Requirements

- Windows 10 (build 19041 or later) or Windows 11, 64-bit.
- Both PCs on the **same network and subnet** — e.g. both `192.168.1.x`. If you
  are unsure, run `ipconfig` on each; the first three numbers must match.

Nothing else. The installer carries everything the app needs, so a fresh Windows
machine works.

---

## Install

You need `KRemote-Setup-1.1.0.exe`. If somebody sent it to you, skip to
**[Installing](#installing)**. If you have the source code instead, build it
once with the step below.

### Getting the installer file

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

This takes a minute or two and produces **`dist\KRemote-Setup-1.1.0.exe`** — one
self-contained file, about 43 MB, that installs on any Windows 10/11 64-bit PC.

It needs two things installed first:

1. **The .NET 9 SDK** — <https://dotnet.microsoft.com/download/dotnet/9.0>
2. **Inno Setup 6**:

   ```powershell
   winget install --id JRSoftware.InnoSetup --source winget
   ```

   Close and reopen your terminal afterwards.

If it stops with an error:

| Message | What to do |
| --- | --- |
| `Inno Setup 6 not found` | Install it with the command above, then open a **new** terminal and try again. |
| `dotnet publish failed` | The .NET 9 SDK is missing. Check with `dotnet --version`. |

To stamp a different version number on the file, add `-Version 1.2.0`.

### Installing

Run `KRemote-Setup-1.1.0.exe` and click through it. It:

- installs for you only, **without asking for an administrator password**,
- puts a KRemote shortcut on your desktop and in the Start Menu,
- offers to **allow KRemote through Windows Firewall** — this one step does ask
  for admin confirmation, and you should say yes. It is what lets the other PC
  find you. Without it, scans come back empty.

**Do this on both PCs.** Copy the same `KRemote-Setup-1.1.0.exe` to the second
machine — USB stick, shared folder, whatever is easiest — and run it there. It
needs nothing preinstalled.

### Uninstalling

*Settings → Apps → KRemote → Uninstall*.

Your saved messages and settings are deliberately left behind, in case you
reinstall. To remove those too, delete the folder `%AppData%\KRemote`. Files you
received are never touched — they stay in your Downloads folder.

---

## Using it

The window has three tabs — **Inbox**, **Saved** and **Settings** — and a
**Share** button in the bottom-right corner that opens the send window.

The first time you start KRemote it asks whether you want to set a PIN. You can
skip this and turn one on later. See [PIN](#pin).

### Sending something

Press **Share** (or **Ctrl+N**).

1. **Find the other PC.** Press **Scan**. It takes about a second and lists every
   PC on your network running KRemote.

   If the one you want does not appear, type its IP address or its computer name
   into the box and press **Add**.

2. **Click it** in the list. If it shows a lock, type that PC's PIN and press
   **Unlock** first.

3. **Write your message.** Any combination of:
   - a **Title** (optional) — becomes the bold heading in the other person's inbox,
   - **text** — type or paste anything,
   - **files** — press *Attach files* and pick one or several.

4. **Press Send** (or **Ctrl+Enter**). A bar shows the progress, and the speed
   while it transfers.

Your message and attachments are cleared only after the send succeeds, so nothing
is lost if the other PC turns out to be unreachable.

> **The device list is a snapshot, not a live view.** It shows who answered the
> last time you pressed Scan. If you open KRemote on the other PC afterwards,
> press Scan again. Your own PC never appears in its own list.

### Sending several files at once

You can attach as many files as you like. How they travel is up to you, under
*Settings → Sending*:

- **Zip them into one archive** (the default) — everything is bundled into a
  single compressed file. The other person receives one `.zip`.
- **Send as separate files, grouped into one message** — the files arrive
  individually but appear as a single entry in the inbox, listing all of them.

Zip is usually the better choice: it is one transfer, and it is smaller.

### Receiving

Anything sent to you appears in your **Inbox**, newest first, marked unread until
you click it. If it is a large file you will see it arriving.

Depending on your settings, you also get a Windows notification, a sound, a
flashing taskbar icon, and a count on the Inbox tab.

Click any item to read it — the full text, or the file's name, size, who sent it
and where it was saved.

| Button | What it does |
| --- | --- |
| **Open** | Opens the received file in whatever program it belongs to. |
| **Show in folder** | Opens File Explorer with the file highlighted. |
| **Copy** | Copies the message text — or, for a file, its location. |
| **Save** | Moves the message to the **Saved** tab, so it is still there next time you open KRemote. |
| **Delete** | Removes it from the list (the **Del** key works too). |

### Keeping messages

**Your inbox is cleared when you close KRemote.** Anything you want to keep needs
the **Save** button.

The two tabs never show the same message. **Inbox** is what has arrived since you
opened the app; **Saved** is what you have chosen to keep. Pressing *Save* moves a
message from the first to the second, and *Remove from Saved* moves it back for
the rest of the session. So when you open KRemote, the Inbox starts empty and
everything you kept is waiting under Saved.

**Deleting a message never deletes the file.** A received file is already on your
disk; the inbox entry is just a record of it. To delete the file itself, use
*Show in folder* and delete it there.

Received files never overwrite anything you already have. A second `report.pdf`
is saved as `report (2).pdf`.

### Where your files go

Received files are saved to:

```
C:\Users\<you>\Downloads\KRemote
```

You can change that under *Settings → Downloads folder*.

### Settings

| Setting | What it does |
| --- | --- |
| **Display name** | A friendlier name other PCs see instead of your Windows computer name. Optional. |
| **Downloads folder** | Where received files are saved. |
| **Multiple files** | Zip several files into one archive, or send them separately as a group. |
| **Group timeout** | How long to wait for the rest of a group before giving up on it. |
| **Notifications** | Windows notification, sound, taskbar flash, and the unread count — each can be switched off on its own. |
| **PIN** | See below. |

Changes take effect immediately; there is no Save button.

### PIN

Switching on a PIN means anyone sending to your PC has to enter your 4-digit code
first. They are asked once, and again after they rescan.

**Think of this as a way to avoid mistakes, not as protection.** It reliably stops
a colleague sending a file to the wrong machine. It is not real security: the
code is short, it is stored and sent as plain text, and nothing limits how many
times someone can guess. Do not rely on it to keep anyone out.

---

## A note on privacy

KRemote sends everything in plain form across your network, with no encryption.
Anyone on the same network who is looking can read it, and any PC that can reach
yours can put a message in your inbox and a file in your downloads folder.

That is a deliberate trade — it is why the app needs no accounts, no setup and no
internet. **Use it on a network you trust: your home or your office.** Do not use
it on café, hotel, airport or other public Wi-Fi.

Two things KRemote does protect you from:

- **A received file cannot escape your downloads folder**, whatever it claims to
  be called.
- **Nothing you receive is ever run automatically.** Files are only written to
  disk.

But **Open** launches a file exactly as double-clicking it would. Treat a file
that arrives in KRemote the same way you would treat one arriving by email.

---

## If something goes wrong

| Problem | What is happening |
| --- | --- |
| **Scan finds nothing** | KRemote is not open on the other PC, or it was not allowed through Windows Firewall there. Reinstall on that PC and accept the firewall step, and make sure the app is running. |
| **Scan still finds nothing, firewall is fine** | The two PCs are probably on different networks — one on Wi-Fi, one on Ethernet, for example. Run `ipconfig` on both: the first three numbers of the IP address must match. You can also try adding the address by hand in the Share window. |
| **A PC that was there has disappeared** | The list is only as fresh as your last scan. Press Scan again. |
| **"Could not send… No connection could be made"** | The other PC closed KRemote after your last scan. Press Scan again. |
| **"Could not send… Incorrect PIN"** | That PC's PIN changed, or your unlock expired when you rescanned. Select it again and re-enter the PIN. |
| **I am sending fine but never receive anything** | Another copy of KRemote may already be running on your PC and holding the connection. Close the extra copy. |
| **It does not work on guest or public Wi-Fi** | Most public networks deliberately stop devices talking to each other. Nothing in the app can get around it. |
| **Where did my file go?** | `Downloads\KRemote`, unless you changed the folder. Select the message and press *Show in folder*. |
| **I deleted the message but the file is still there** | That is intended — deleting a message never deletes the file. Remove it from File Explorer. |
| **A transfer stopped partway** | Nothing is left behind, and no half-finished file is saved. Just send it again. |
| **The file arrived named "(2)"** | You already had a file with that name. KRemote never overwrites. |
| **Only some of the files arrived** | The rest did not make it in time. Increase the group timeout in *Settings → Sending*, or switch to zip mode, which sends everything in one go. |
| **The installer says KRemote is already installed** | Install over the top — it upgrades in place. |

---

## License

MIT — see [LICENSE](LICENSE).
