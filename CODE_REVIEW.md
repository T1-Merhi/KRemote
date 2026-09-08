# KRemote — Code Review and Refactoring Plan

Reviewed at commit `5da8196`, .NET 9 / WPF, 1.1.0. The solution builds clean:
0 warnings, 0 errors.

This document is a proposal. Nothing in the codebase has been changed yet.

---

## 1. Verdict

The networking layer is the strongest part of this codebase and should mostly be
left alone. The protocol is well framed, the file receive path is genuinely
careful (`.part` staging, SHA-256 verification, per-chunk stall timeout, refusal
before the first byte moves), and `LineIO`'s byte-at-a-time header read is a
deliberate, correct choice rather than an oversight.

The weakness is entirely on the UI side, and it is a *layering* problem rather
than a correctness one. `MainWindow.xaml.cs` (546 lines) and
`SharePopup.xaml.cs` (465 lines) are the only two places where application
state, persistence, formatting and widget manipulation all live in the same
method bodies. That is what makes the code feel larger than the feature set
justifies, and it is where nearly all the duplication sits.

Roughly 250 lines can be removed without losing a single behaviour.

### Scope agreed for this plan

| Decision | Chosen approach |
| --- | --- |
| MVVM depth | **Extract services, keep code-behind.** No ViewModels, no `ICommand`, XAML bindings stay as they are. Code-behind stays the layer that talks to controls. |
| Inbox/Saved duplication | **One reusable `UserControl`**, used twice. |
| Root installer set | **Delete it**, keep `installer/` as the single path. |
| Defensive code | **Keep every network/filesystem check. Cut UI defensiveness only.** |

---

## 2. What is already good (do not "improve" these)

These are called out explicitly so a later pass does not undo them in the name
of simplification.

- **`LineIO.ReadLineAsync` reading one byte at a time.** It looks inefficient
  and it is the only correct option: a buffered reader would swallow raw file
  bytes that follow the header on the same socket. Headers are ≤ 64 KB and read
  once per connection, so the cost is irrelevant.
- **`FileNaming.Sanitize`.** Directory stripping, invalid-char replacement,
  reserved-name prefixing and the length cap are the entire defence against a
  hostile peer writing outside `Downloads\KRemote`. Untouchable.
- **`.part` staging plus `File.Move(overwrite: false)`.** An interrupted
  transfer leaves nothing behind instead of a truncated file that looks real.
- **Per-chunk `timeout.CancelAfter(...)`.** This limits *silence*, not transfer
  duration, so a slow link is never killed for being slow.
- **`DrainCloseAsync`'s half-close.** This is what stops the final `ok` being
  lost to an RST — the fix behind commit `4f036b8`.
- **The `ready` handshake on both text and file.** Lets the receiver refuse
  before the sender has pushed a payload.
- **`Protocol.Deserialize` returning `null` on `JsonException`.** That is not a
  redundant null check; it is the parser boundary for untrusted input.

---

## 3. Findings

Ordered by how much they cost the reader.

### F1 — The Inbox and Saved tabs are duplicated twice over

**Where:** `MainWindow.xaml:28-150`, `MainWindow.xaml.cs` throughout.

Two tabs differ only in their heading, their empty-state glyph and sentence, one
button (Save vs. Remove from Saved), and which subset of messages they show.
Everything else is copied:

- 62 lines of near-identical XAML per tab.
- Twin control names for the same concept: `InboxStatus`/`SavedStatus`,
  `InboxList`/`SavedList`, `MessageView`/`SavedMessageView`,
  `MessagePlaceholder`/`SavedMessagePlaceholder`, `InboxEmptyState`/`SavedEmptyState`.
- Twin selection handlers (`InboxList_SelectionChanged`, `SavedList_SelectionChanged`)
  with identical bodies.
- `UpdateMessageButtons()` computes the same four enable-states twice, once per
  tab, in one method.
- `SetStatus` has to branch on `RootTabs.SelectedIndex == 1` to decide which of
  the two status labels to write to, and `ActiveList` branches on the same index
  to decide which `ListBox` is live.

That last pair is the tell: the duplication has leaked into a *tab-index
conditional*, which is a fragile way to answer "which pane am I in". Adding a
tab renumbers it.

**Proposal.** One `Views/MessageListPane.xaml(.cs)` `UserControl` owning: the
heading, status line, list, empty state, action buttons, and detail pane. It
exposes what differs as ordinary CLR properties set from XAML:

```xml
<local:MessageListPane x:Name="InboxPane"
                       Heading="Inbox"
                       EmptyGlyph="&#xE8BD;"
                       EmptyText="Nothing here yet. …"
                       Mode="Inbox"/>
```

`SetStatus` and `ActiveList` then disappear entirely — each pane owns its own
status line, and the active pane is simply whichever one raised the event.

**Removes:** ~62 lines of XAML and ~120 lines of C#.

---

### F2 — `MainWindow` is four unrelated responsibilities in one class

**Where:** `MainWindow.xaml.cs`, all 546 lines.

The class currently is, at once:

1. the inbox collection and its save/unsave/delete rules,
2. the persistence policy (when to write `saved-messages.json`),
3. the entire Settings tab (13 handlers, ~120 lines, lines 400-539),
4. the window itself — server lifecycle, notifications, keyboard shortcuts.

None of (1)-(3) needs a `Window` to exist, and none of them can currently be
reasoned about without one.

**Proposal — three services, per the agreed scope.** These are plain classes; no
`INotifyPropertyChanged` plumbing beyond what `InboxMessage` already has.

```
Services/InboxService.cs
    ObservableCollection<InboxMessage> Messages    (the single source of truth)
    ICollectionView Saved                          (the filtered view)
    int UnreadCount
    void Add(InboxMessage) / Save(m) / Unsave(m) / Remove(m) / MarkRead(m)
    event Action? Changed
    Loads at construction, persists on every mutation.

Services/SettingsService.cs
    AppSettings Current
    void Update(Action<AppSettings> change)        (mutate + persist in one call)
    Wraps SettingsStore; owns the save-on-change policy.

Services/SendCoordinator.cs
    Task<TransferResult> SendAsync(Peer, string title, string text,
                                   IReadOnlyList<StagedAttachment>, IProgress<…>, CancellationToken)
    Owns validation limits, PIN selection, text-vs-attachment branching,
    and the elapsed/rate arithmetic now inlined in SharePopup.
```

`MainWindow` keeps its handlers but each becomes one or two lines that call a
service and refresh the UI. Expected: **546 → ~200 lines**, with the removed
logic moved rather than deleted.

---

### F3 — The persist-and-refresh sequence is copy-pasted four times

**Where:** `MainWindow.xaml.cs:286-335` (`SaveButton_Click`, `UnsaveButton_Click`,
`DeleteButton_Click`) and the constructor.

Every mutation repeats the same five-step ritual:

```csharp
message.IsSaved = true;
_savedView.View.Refresh();
if (PersistSaved()) { UpdateInboxStatus(); UpdateSavedStatus(); }
UpdateMessageButtons();
UpdateEmptyStates();
```

Four `Update*` methods must be called in the right combination at eight call
sites. `OnMessageReceived` calls four of the five; `SaveButton_Click` calls a
different four. Any future mutation has to rediscover the correct set.

**Proposal.** `InboxService` raises one `Changed` event after any mutation; the
window subscribes once with a single `RefreshUi()`. The ritual collapses to:

```csharp
private void Save_Click(object sender, RoutedEventArgs e) => _inbox.Save(Selected);
```

**Removes:** ~40 lines, and the whole class of "forgot to call `UpdateEmptyStates`" bug.

---

### F4 — The `_loadingSettings` flag is a symptom, not a solution

**Where:** `MainWindow.xaml.cs:29, 400-505`.

`LoadSettingsIntoUi()` sets `_loadingSettings = true` so that the eight
`*_Changed` handlers it is about to trigger do not immediately write the
settings file back. Every one of those handlers then opens with
`if (_loadingSettings) return;`. That is one flag, one guard repeated eight
times, and a lifetime that is easy to get wrong (an early `return` or a throw
between the two assignments leaves settings permanently unsaveable).

Additionally, the four notification handlers all funnel into
`NotificationSetting_Changed`, which rewrites **all four** settings regardless of
which checkbox changed.

**Proposal.** With `SettingsService.Update(...)` owning persistence, populate
the controls by *detaching* nothing and instead having each handler be
idempotent — write the one value it owns, and let `Update` no-op when the value
is unchanged. The flag goes away with the guards.

**Removes:** ~25 lines and one piece of mutable window state.

---

### F5 — UI-layer defensiveness that cannot fire

Per the agreed scope, this is where the null-check pruning applies — the network
layer keeps everything.

| Location | Issue |
| --- | --- |
| `MainWindow.xaml.cs:229, 249, 273, 288, 303, 318` | Six handlers repeat `if (ActiveList.SelectedItem is not InboxMessage … ) return;`. The buttons are already `IsEnabled=false` when nothing is selected, so the guard is unreachable in practice. With the `MessageListPane` of F1, the pane hands the handler a non-null selection. |
| `MainWindow.xaml.cs:271-284, 523-534` | Two near-identical `try/catch(Exception)` blocks around `Clipboard.SetText`. Worth one shared `TryCopy(string, string)` helper rather than two catch-alls. |
| `MainWindow.xaml.cs:23-24` | `_pinManager` and `_notifications` are `null!` fields assigned in the constructor. Both can be `readonly` and assigned inline once the first-run prompt moves ahead of them. |
| `MainWindow.xaml.cs:116-120` | `RootTabs_SelectionChanged` hides the Share button using a magic `SelectedIndex == 2`. Compare against the `TabItem` instead. |
| `SharePopup.xaml.cs:55-59, 105-109, 238-241` | Three hand-written placeholder-visibility handlers. `App.xaml` already declares a `BooleanToVisibilityConverter` (`x:Key="BoolToVis"`, line 7) that appears to be unused — these are one XAML trigger each. |
| `InboxMessage.Flatten` (`Models/InboxMessage.cs`) | `while (flat.Contains("  ")) flat = flat.Replace("  ", " ");` is an O(n²) loop over a string that is then truncated to 70 chars. A single `Regex` or a `Split`/`Join` is both shorter and clearer. |
| `PeerServer.cs:30-34` | The `Func<AppSettings>?`/`Func<string>?` constructor parameters both default to null and then to a throwaway `new AppSettings()`. Nothing in the app constructs a `PeerServer` without them. Make them required — the defaults only exist to serve a caller that does not exist. |

---

### F6 — `Peer` and `PeerServer` disagree about what a "name" is

**Where:** `Models/Peer.cs`, `Net/PeerServer.cs:222-227`, `Views/SharePopup.xaml.cs`.

There are three overlapping notions of a peer's name — `MachineName`,
`DisplayName`, and the computed `Label` — and the resolution rule is
reimplemented at each site:

- `Peer.Label` → `DisplayName` if set, else `MachineName`, formatted as `"Display (MACHINE)"`.
- `PeerServer.SenderName` → `DisplayName`, else `Name`, else the raw IP.
- `SharePopup` mixes them: it reports success with `peer.MachineName` into
  `SuccessMessage` (line 343) but with `peer.Label` into the in-window banner
  (line 347). Same event, two different names for the same PC.

**Proposal.** One rule in one place. `Peer.Label` stays the single display
string, `SenderName` reuses the same precedence, and `SharePopup` uses `Label`
for both messages. Low risk, removes a real inconsistency the user can see.

---

### F7 — `SharePopup` mixes validation, transport and formatting in one method

**Where:** `Views/SharePopup.xaml.cs:282-434`.

`SendButton_Click` is 80 lines that validate limits, choose the PIN, branch
text-vs-attachments, drive the transport, compute elapsed time and transfer
rate, and format three separate result strings. `SendAttachmentsAsync` adds
another 60, including the `completedBefore`/`overallTotal` arithmetic that
differs by send mode.

Note also that the two size limits are declared here as private constants
(`MaxAttachmentsTotalBytes` = 1 GB, `MaxTextBytes` = 1 MB) while the *receiver's*
limit lives in `Protocol.MaxTextBytes` = **64 MB**. The sender's 1 MB text cap
and the receiver's 64 MB cap are unrelated numbers in unrelated files. They
should sit together in `Protocol` even if they stay different values.

**Proposal.** `SendCoordinator` (F2) takes the validation and the arithmetic and
returns a `TransferResult` record; `SendButton_Click` becomes await, then render.
Expected: 465 → ~250 lines.

---

### F8 — Small correctness and consistency items

These are not refactors; they are things I noticed while reading.

1. **`PeerServer.cs:259-283` — group assembly double-counts the first file.**
   The `PendingGroup` is constructed with the first attachment's `FileName`,
   `FilePath` and `FileSize` copied into the scalar fields, *and* that same
   attachment is then added to `Attachments`. It happens to render correctly
   today, but a single-file message that arrives with a `groupId` gets
   `Attachments.Count == 1`, so `IsGroup` is false and the scalar fields are the
   ones used. Worth making explicit: either always use `Attachments`, or never.

2. **`PeerServer.cs:274-283` — `pending.Message` is read outside the lock.**
   `Attachments.Add` happens under `lock (pending)`, then `TryRemove` and
   `MessageReceived?.Invoke(pending.Message)` happen outside it. In practice
   the sender is sequential so no second thread is mid-add, but the locking
   discipline is inconsistent with itself.

3. **`SweepStaleGroups` publishes incomplete groups as if complete.** A group
   that times out is delivered with however many attachments arrived, with no
   indication that files are missing. That may well be intended; flagging it as
   a question rather than a defect.

4. **`KRemote.csproj:26** references `KRemote.Setup.wixproj`, which does not
   exist in the repository. Dead reference; delete.

5. **`Zip.CreateTempArchive` temp file can leak.** It is deleted in a `finally`
   in `PeerSender.SendFilesAsync`, which is correct — but if the process is
   killed mid-send the archive stays in `%TEMP%`. Acceptable; noted only for
   completeness.

---

### F9 — Two conflicting installer setups

Per the agreed scope: **delete the root set.**

`installer/KRemote.iss` is the correct one and is what `README.md` documents:
per-user (`PrivilegesRequired=lowest`), a real `AppId`, self-contained x64
payload, the optional self-elevating firewall task, output to `dist\`.

The root set added in `5da8196` is a weaker parallel implementation:

| File | Problem |
| --- | --- |
| `KRemote-Installer.iss` | `{autopf}` with no `PrivilegesRequired` → admin install; placeholder `AppID={{12345678-1234-1234-1234-123456789012}`; `LicenseFile=LICENSE.txt` — the file in the repo is `LICENSE`, so the compile fails; `dotnet publish` without `--self-contained`, contradicting the README's "carries its own .NET"; no firewall step; `OutputDir=Installers`. |
| `Build-Installer.ps1` | Builds via `dotnet build` (not `publish`), then prints an output path — `$ProjectRoot\Installer\KRemote-Setup.exe` — that matches neither the `.iss` `OutputDir` (`Installers`) nor anything else. |
| `Build-Installer.bat` | Wrapper for the above. |
| `QUICK_START.txt` | Hard-codes `C:\Users\Hussein.Merhi\source\T1-Merhi\KRemote` and claims output lands in `Output\`, a third path. |
| `INSTALLER_SETUP.md` | Documents the root flow, competing with the README's `installer/` flow. |

**Proposal.** Delete all five, drop the dead `wixproj` reference from the csproj,
and let `README.md` remain the single source of truth.

---

### F10 — README has drifted a full feature generation behind

`README.md` describes the 1.0 app: three panes in one window, a "Submit"
button, one file at a time, no PIN, no display names, no notifications, no
Settings tab, no SHA-256, no manual address entry, no zip/grouped modes. Its
"Project layout" section omits `Models/AppSettings.cs`, `Models/InboxAttachment.cs`,
`Models/StagedAttachment.cs`, `Net/PinManager.cs`, `Net/Zip.cs`, `Net/FileNaming.cs`,
`Notifications/`, `Views/`, and `Storage/SettingsStore.cs`.

The protocol section is also now wrong: it shows text riding inside the JSON
header, which commit `4f036b8` changed to a length-prefixed body.

---

## 4. Proposed target structure

```
KRemote.sln
KRemote.csproj
App.xaml(.cs)
MainWindow.xaml(.cs)          ~200 lines, was 546
Models/
  AppSettings.cs
  InboxAttachment.cs
  InboxMessage.cs             Flatten() simplified
  Peer.cs                     single naming rule
  StagedAttachment.cs
  TransferResult.cs           NEW — record: bytes, elapsed, rate, file count
Services/                     NEW
  InboxService.cs             collection + persistence + unread + Changed event
  SettingsService.cs          AppSettings + Update(Action<AppSettings>)
  SendCoordinator.cs          validation + PIN + transport + rate arithmetic
Net/                          unchanged except F5/F6/F8 touch-ups
  Protocol.cs                 gains the sender-side limits
  LineIO.cs                   untouched
  FileNaming.cs               untouched
  Zip.cs
  PinManager.cs
  PeerScanner.cs
  PeerSender.cs
  PeerServer.cs
Storage/
  MessageStore.cs
  SettingsStore.cs
Notifications/
  NotificationService.cs
  TaskbarFlash.cs
Views/
  MessageListPane.xaml(.cs)   NEW — used twice, replaces both tabs
  SharePopup.xaml(.cs)        ~250 lines, was 465
  FirstRunPinPrompt.xaml(.cs)
  ToastWindow.xaml(.cs)
installer/                    the only installer path
```

Deleted: `KRemote-Installer.iss`, `Build-Installer.ps1`, `Build-Installer.bat`,
`QUICK_START.txt`, `INSTALLER_SETUP.md`.

---

## 5. Suggested sequence

Each step compiles on its own, so breakage surfaces immediately rather than
accumulating.

| # | Step | Risk | Net lines |
| --- | --- | --- | --- |
| 1 | Delete the root installer set and the dead `wixproj` reference | none | −5 files |
| 2 | `SettingsService` + remove `_loadingSettings` (F4) | low | −25 |
| 3 | `InboxService` + single `Changed` event (F2, F3) | medium | −40 |
| 4 | `MessageListPane` UserControl, used twice (F1) | medium | −180 |
| 5 | `SendCoordinator` + `TransferResult` (F7) | medium | −60 |
| 6 | UI defensiveness cleanup, `Flatten`, `Peer` naming (F5, F6) | low | −30 |
| 7 | `PeerServer` group-assembly tidy-up (F8.1, F8.2) | low | ~0 |
| 8 | Rewrite `README.md` to match the app (F10) | none | docs |

Expected total: **roughly 335 lines of C#/XAML removed**, no behaviour changed.

---

## 6. UX problems in what is already built

Everything above concerns how the code is organised. This section and the next
concern what the app *does*. These are inferences from reading the code — the
app has not been exercised — so they are stated as observations about behaviour
the code implies, not as reports from use.

Unlike sections 3-5, none of this is required to land the refactor. It is
recorded here so the two conversations stay in one document.

### U1 — The device list silently rots

`PeerScanner.ScanAsync` is called from exactly one place, `ScanButton_Click`.
There is no background refresh and no announcement broadcast, so the list is a
snapshot of who answered at one moment, presented as if it were a list of who is
available now. A peer that has since closed KRemote still shows, and the user
only discovers this when a send fails.

The README acknowledges this — *"The list is a snapshot, not a live feed"* — but
documenting a broken mental model does not repair it. **This is the highest-value
functional gap in the app.**

### U2 — The Share popup is modal

`MainWindow.ShareButton_Click` calls `popup.ShowDialog()`, which blocks the
whole application for the lifetime of the dialog. During a large transfer the
user cannot read their inbox, open a received file, or change a setting. Making
the window modeless is a small change that removes a real irritation.

### U3 — One send produces two confirmations

`SharePopup` shows its own in-window success banner (`ShowSuccess`, line 346).
Then, when the dialog closes, `MainWindow.ShareButton_Click` opens
`ToastWindow` — *also* via `ShowDialog()` — to say the same thing again, and
that second one must be clicked to dismiss. Two confirmations for one action,
the second of them blocking.

### U4 — A transfer cannot be cancelled

`PeerSender.SendFilesAsync`, `SendTextAsync` and `VerifyPinAsync` all accept a
`CancellationToken`, and **every call site passes `CancellationToken.None`**
(`SharePopup.xaml.cs:224, 333, 422`; `ScanButton_Click:81`). The plumbing is
already in place and simply unused, so a 1 GB send cannot be stopped short of
killing the process. A cancel button is close to free.

### U5 — The PIN reads as security but is not

As implemented, `PinEnabled` stops honest mistakes and nothing more:

- stored in plaintext in `settings.json` (`AppSettings.Pin`),
- transmitted in plaintext in every `text` and `file` frame,
- compared with `frame.Pin == _currentPin()` in `PeerServer.IsPinCorrect` — an
  ordinary string comparison, notably inconsistent with the SHA-256 path a few
  lines below which correctly uses `CryptographicOperations.FixedTimeEquals`,
- four digits, so 10,000 possibilities,
- no attempt limiting, delay, or lockout anywhere in `PeerServer`.

A brute-force sweep of the whole keyspace over a LAN is trivial. This is not
necessarily wrong for the threat model the README describes, but the UI presents
it as protection. Either reword it as what it is ("stops a housemate sending to
the wrong PC") or make it real — at minimum a constant-time compare and a
per-address attempt limit.

### U6 — Files are accepted with no consent step, and opened with no warning

Any peer that can reach port 5555 writes into the downloads folder unprompted.
`FileNaming.Sanitize` correctly prevents escaping that folder, but nothing
distinguishes a PDF from a `.exe` or `.ps1`, and the **Open** button launches
whatever it is with `UseShellExecute = true`
(`MainWindow.xaml.cs:239`). Two independent mitigations worth considering: an
optional "ask before accepting" toggle, and a warning when opening an
executable extension.

### U7 — Settings are written to disk on every keystroke

`DisplayNameBox_TextChanged` and `PinBox_TextChanged` each call `SaveSettings()`,
which serialises the whole `AppSettings` object and rewrites `settings.json`.
Typing a twelve-character display name performs twelve full file writes. A short
debounce, or saving on focus-loss, is sufficient.

### U8 — A failed listener is invisible

`MainWindow.StartServer` wraps `_server.Start()` in
`catch (SocketException) { }` — an empty catch. If port 5555 is already taken
the app looks completely healthy and simply never receives anything, with no
indication why.

Worth noting that `README.md` documents a specific error message for exactly
this case (*"Port 5555 is already in use…"* in the troubleshooting table) that
**the code does not produce**. Either the message was lost in a refactor or it
was never implemented.

---

## 7. Functionality that is missing

Ordered by my estimate of value per unit of work. None of this is required by
the refactor; it is here to inform what the refactored structure should make
easy.

| | Feature | Why it matters | Rough cost |
| --- | --- | --- | --- |
| M1 | **Background peer refresh** | Fixes U1, the worst gap. A periodic re-scan, or a UDP announcement on start/exit so peers appear and disappear on their own. | Medium |
| M2 | **Drag and drop onto the window** | The app exists to move files, yet the only way to attach one is a file dialog. `AllowDrop` plus a `Drop` handler feeding `_attachments`. | Small |
| M3 | **Cancel button during transfer** | U4 — the `CancellationToken` plumbing already exists and is passed `None` everywhere. | Small |
| M4 | **Send from the shell** | A "Send to → KRemote" context-menu entry, or a CLI (`KRemote.exe --send file.pdf --to DESKTOP-B`). Today every transfer begins by opening the app. | Medium |
| M5 | **Send history** | The app records what arrived but not what was sent. "Did that go through?" is answerable only from a banner that disappears with the dialog. | Small |
| M6 | **Reply to a message** | `InboxMessage.FromAddress` is already stored, so replying is one click; today it means reopening Share and re-finding the peer. | Small |
| M7 | **Minimize to tray** | A background receiver that requires an open window is a background receiver people close. | Small |
| M8 | **Clipboard send** | The natural companion to a LAN text tool: a hotkey that sends the clipboard to the last-used peer. | Small |
| M9 | **Send a folder** | Files only today. Zip mode already exists, so this is mostly a directory walk feeding the existing path. | Small |
| M10 | **Resume an interrupted transfer** | A dropped transfer discards the `.part` file and restarts. Defensible on a LAN, painful near the 1 GB limit. | Large |

### If only three

**M1, M2, M3.** The first repairs a broken mental model, the second removes
friction from the app's core action, and the third finishes plumbing that is
already written.

### Interaction with the refactor

Two of these are meaningfully cheaper to build *during* the refactor than after:

- **M5 (send history)** wants to live in `SendCoordinator`, which step 5
  creates. Adding it afterwards means reopening that class.
- **U2 (modeless Share)** is easiest while `SharePopup` is already being
  restructured in step 5, since it changes how the result is returned to
  `MainWindow`.

If either is wanted, say so before step 5 rather than after.

---

## 8. Open questions

I would rather ask than assume. The first five affect the refactor; the last two
affect scope.

1. **F8.3 — stale groups.** When a grouped multi-file send times out, should the
   partial group still appear in the inbox as it does now, or should it be
   marked as incomplete (e.g. "3 of 5 files")?

2. **F7 — the sender's 1 MB text cap** versus the receiver's 64 MB
   (`Protocol.MaxTextBytes`). Is the 1 MB limit deliberate, or should both sides
   agree on one number?

3. **`MessageListPane` and the Saved tab's detail pane.** Both tabs currently
   have an independent detail `TextBox`, so selecting a message in Inbox and
   switching to Saved shows two different selections. Should the unified control
   preserve that (independent selection per tab) or share one selection?

4. **Step 8 — the README.** Is rewriting it in scope for this pass, or do you
   want the code refactor landed first and the docs handled separately?

5. **`FirstRunPinPrompt` is shown from the `MainWindow` constructor**
   (`MainWindow.xaml.cs:37-45`), before the window exists, so it has no `Owner`
   and appears unparented. Worth moving to `App.OnStartup` as part of step 2, or
   leave it?

6. **Sections 6 and 7 — are any of these in scope now?** The refactor as planned
   changes no behaviour. U2-U4 and M5 are cheap enough to fold into the relevant
   step; everything else is better as separate work afterwards.

7. **U5 — what is the PIN actually for?** If it is meant as convenience (avoid
   sending to the wrong PC), the fix is wording. If it is meant as security, it
   needs a constant-time compare and attempt limiting at minimum. The answer
   decides whether this is a docs change or a code change.
