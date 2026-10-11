# Correcting a document before it posts

Author: ui-ux-designer. Status: Decided (design); the three owner confirmations in section 10 have a default and do not block the build.
Closes: NR-02 (Major, `docs/qa/retest-novice-warehouse-staff.md`), PR-02 and PS-11 (`docs/qa/retest-power-supervisor.md`), UX question Q3 (`docs/design/mvp-ux.md`).
Reads: `src/Ims.Domain/Stock/Models/StockDocument.cs`, `Services/DocumentActionPolicy.cs`, `docs/architecture/backend.md`, `DocumentsController.cs`.

## 0. The decision in ten lines

1. A mistake seen before posting is fixed by going back to Draft, never by Cancel and retyping. Same document number, same lines.
2. New command **recall** (button label **Edit**): the submitter takes their own Submitted document back to Draft. Allowed while it is Submitted, whether or not it needs approval. Nothing was approved yet, so nothing is voided. No reason asked.
3. New command **send back** (button **Send back**): on an Approved document, a person who may review it withdraws the approval and returns it to the creator as a Returned draft, reason required. The approval is voided (kept in the audit log only).
4. **Cancel** is allowed on Approved for the same people (Supervisor or Admin; Owner or Admin for Large), reason required. It fixes PR-02.
5. The creator can not pull back an Approved document alone: an approval is someone else's decision, so the approver (or a role above) undoes it. The creator sees who to ask.
6. Posted stays reversal-only (Supervisor or Admin, reason required). Reversed and Cancelled stay terminal.
7. The Post dialog buttons become **[Edit items] [Post]** (label **Back** only when the reader cannot recall). Edit items recalls at once and opens the editor on the same document. Closing the dialog (Esc, outside tap, system Back) leaves a Submitted page that has **Edit** next to **Post**, so no read-only dead end exists.
8. Edit is a visible bar button (primary on a document waiting for approval, secondary next to Post otherwise). Cancel document stays under "..." because it is no longer the common recovery.
9. Rule for confirmations: ask only when someone else's work is affected. Recall of an approval-free document: no dialog. Recall of a document waiting for approval: one short dialog. Send back and Cancel on Approved: dialog with a required reason.
10. No new columns. Recall leaves no field behind (audit row only). Send back reuses the Returned fields, so the creator's "Returned" badge, banner and Home count work unchanged.

## 1. State machine for corrections

Legend: R = reason required (at least 5 characters once trimmed, `Limits.ReasonMinLength`). "Reviewer" = a role that may review that document: Supervisor or Admin when it is not Large, Owner or Admin when it is Large (`ApprovalPolicy.MayReview`).

```
              Submit                    Approve                 Post
 DRAFT ------------------> SUBMITTED ------------------> APPROVED ------------> POSTED --Reverse (R)--> REVERSED
   ^  ^                      |  |  |                         |  |
   |  | Recall (submitter)   |  |  | Reject (R, reviewer)    |  | Send back (R, reviewer): approval voided
   |  +----------------------+  |  +-> DRAFT (Returned)      |  +-> DRAFT (Returned)
   |                            |                            |
   |  Cancel (creator/submitter,|supervisor)                 | Cancel (R, reviewer)
   v                            v                            v
 CANCELLED <--------------------+----------------------------+
```

| State | Action | Who | Condition | Result | Reason | Audit action |
|---|---|---|---|---|---|---|
| Draft | Edit, Submit | Creator (Staff, Clerk); Supervisor, Admin any | unchanged | unchanged | no | existing |
| Draft | Cancel | Creator; Supervisor, Admin any | unchanged | Cancelled | optional (unchanged) | `document.cancelled` |
| Submitted | **Recall** | **The submitter, any role that may create** (Staff, Clerk, Supervisor, Admin) | status is Submitted; document in scope; token current | Draft. `SubmittedBy/At`, `RequiresApproval`, `IsLarge`, `RequiresOverride` cleared. No `RejectionReason`, so no "Returned" badge. Document number and lines unchanged | no | **`document.recalled`** |
| Submitted | Reject | Reviewer, not the submitter unless self-approval is on | unchanged | Draft (Returned) | R | `document.rejected` |
| Submitted | Approve | Reviewer | unchanged | Approved | R only for override | `document.approved` |
| Submitted | Post | Anyone who may post, approval-free only | unchanged | Posted | no | `document.posted` |
| Submitted | Cancel | Submitter; Supervisor, Admin any | unchanged | Cancelled | optional | `document.cancelled` |
| Approved | Post | Supervisor, Admin | unchanged | Posted | no | `document.posted` |
| Approved | **Send back** | **Reviewer** (Supervisor or Admin; Owner or Admin if Large). The submitter may do it when they are a reviewer role (it only reduces what was allowed, so BR-07 is not at risk) | status is Approved | Draft (Returned): `RejectionReason`, `RejectedBy/At` set; `ApprovedBy/At`, `NegativeStockOverrideReason`, submit fields and flags cleared. Resubmit re-evaluates every approval rule from scratch, needs a new approver | **R** | **`document.approval-withdrawn`** |
| Approved | **Cancel** | **Reviewer** (same set as Send back) | status is Approved | Cancelled. `ApprovedBy/At` stay on the row as history | **R** | `document.cancelled` (before status Approved) |
| Approved | Edit, Recall | nobody | | not offered; Staff and Clerk see "Ask {approver} to send it back" | | |
| Posted | Reverse | Supervisor, Admin (as today) | E6 still applies | Reversed | R | `document.reversed` |
| Reversed, Cancelled | none | | terminal | | | |

Approvals already given. Recall happens before any approval, so none exist. Send back voids the approval (fields cleared, audit row keeps who approved and when). Cancel keeps the approval on the row, because the document is finished and the history should show it. A resubmitted document always goes through approval again, so recall and send back can never be used to slip a changed quantity past a check.

Races (all use the existing `If-Match` row token, the loser gets the existing E4 stale banner and Reload):
- Staff recalls while a supervisor taps Approve: whoever commits first wins. If Approve wins, Staff sees the stale banner, Reload shows Approved with "Approved by {name}. Ask {name} to send it back."
- Supervisor posts while Staff taps Edit on an approval-free document: if Post wins, Edit gets the stale banner and the document shows Posted with the existing "Ask a supervisor to reverse" hint.
- A recalled document leaves the approvals inbox at once; a reviewer who still has the old row open gets the stale banner on Approve, and the page shows the Draft (section 5.4).

## 2. UI

### 2.1 Where the action lives

Rule from mvp-ux 7.2 stays: at most one primary and one secondary in the bar; the rest in "...". Secondary sits left of primary.

| State, reader | Phone bar | Desktop header (right) | In "..." |
|---|---|---|---|
| Submitted, approval-free, submitter who may post | `[Edit] [Post]` | same | Cancel document |
| Submitted, approval-free, Clerk (cannot post) | `[Edit]` primary | same | Cancel document |
| Submitted, approval-free, other poster | `[Post]` | same | Cancel document |
| Submitted, waiting for approval, submitter | `[Edit]` primary (text above: "Waiting for approval") | same | Cancel document |
| Submitted, waiting, reviewer | `[Reject] [Approve]` (unchanged) | same | Cancel document |
| Approved, Staff or Clerk or Owner on non-Large | no bar; hint text with approver name | same | none |
| Approved, reviewer who may post | `[Send back] [Post]` | `[Send back] [Cancel document] [Post]` | Cancel document (phone) |
| Approved, reviewer who may not post (Owner, Large) | `[Send back]` primary | `[Send back] [Cancel document]` | Cancel document (phone) |
| Approved, reviewer, post would fail (shortage) | same bar, plus the shortage banner below carries `[Send back] [Cancel document]` as buttons | same | |

"Edit" is the name Staff look for (NR-02 suggestion, `common.edit` already exists in en, zh, ms). The dialog text says what it really does (goes back to draft), so the short button label is not misleading.

### 2.2 Wireframes (phone, 360px)

Submitted, approval-free (the Back-from-Post-dialog landing page, with NR-04 layout: Items first, one line of people):

```
+------------------------------------+
| < Receipts     RCV-WH01-2610-0372  |
| [Submitted]  Ready to post         |
| Wrong quantity? Tap Edit.          |
+------------------------------------+
| Items (2)   Details   History      |
| DEMO-001   Cable 2m      110 PCS   |
| DEMO-002   Plug           4 PCS    |
+------------------------------------+
| Main warehouse. Submitted by you,  |
| 11 Oct 10:12                       |
+====================================+
| [ Edit ]              [ Post ]     |   sticky bar, 56px high, targets >= 48px
+------------------------------------+
```

Post dialog (opened by Submit & post, or by Post):

```
+------------------------------------+
| Post RCV-WH01-2610-0372?           |
| Stock will increase for 2 lines.   |
| Includes DEMO-001 x 110 PCS and    |
| DEMO-002 x 4 PCS.                  |
| Posted documents can't be edited,  |
| only reversed.                     |
|                                    |
| [ Edit items ]        [ Post ]     |
+------------------------------------+
```

Behaviour of the dialog: `Edit items` = recall, then the editor opens (below). `Post` = unchanged. Esc, tap outside, system Back = close the dialog and stay on the Submitted page above (nothing changes on the server). For a reader who cannot recall (a supervisor opening somebody else's document) the left button reads `Back` and just closes.

Editor right after Edit items (same URL, same number, status Draft):

```
+------------------------------------+
| < Receipts     RCV-WH01-2610-0372  |
| [Draft]                            |
+------------------------------------+
| Back to draft. Fix it, then submit |   toast, 4 s, also announced
| again.                             |
| Items (2)                          |
| DEMO-001  [ 110 ]  PCS             |   quantity field selects its text on focus (NR-01)
| ...                                |
+====================================+
| [ Save draft ]   [ Submit & post ] |
+------------------------------------+
```

Submitted, waiting for approval, Staff (submitter):

```
+------------------------------------+
| [Submitted]  Awaiting approval     |
| Why: quantity reaches 100.         |
| Approver: Supervisor.              |
| Need to change something? Tap Edit.|
| ...items...                        |
+====================================+
| [            Edit            ]     |
+------------------------------------+
```

Edit dialog (only here, because the document is in the approver's list):

```
| Edit RCV-WH01-2610-0372?           |
| It leaves the approval list and    |
| goes back to draft. Submit it      |
| again when it is right.            |
| [ Keep waiting ]       [ Edit ]    |
```

Approved, Staff (cannot post, cannot recall):

```
| [Approved]  Approved by Lim        |
| A supervisor will post it.         |
| Wrong quantity? Ask Lim to send    |
| it back.                           |
```

Approved issue that cannot post now, Supervisor (PR-02):

```
| [Approved]  Approved by Lim        |
+------------------------------------+
| (!) Can't be posted now.           |   amber, role="alert" on load
| QA-001 needs 2,000 PCS, but only   |
| 0 are on hand in Main warehouse.   |
| Wait for stock, send it back, or   |
| cancel it.                         |
| [ Send back ]  [ Cancel document ] |
+------------------------------------+
| ...items (QA-001 short by 2,000)...|
+====================================+
| [ Send back ]          [ Post ]    |   Post stays enabled: stock may arrive; the server decides
+------------------------------------+
```

The banner shows when the document is an Issue, `lines[].available < baseQty` for any line (lines of one product add up, as the API already does), and no override was approved (`negativeStockOverrideReason` is empty). It uses data the API already returns (`lines[].available`); no new field.

Send back dialog (reviewer):

```
| Send RCV-... back?                 |
| Your approval is withdrawn. It     |
| goes back to Raj as a draft with   |
| your reason, and needs approval    |
| again.                             |
| Reason  [____________________]     |   required, 5+ characters, focused
| [ Keep approved ]   [ Send back ]  |
```

Cancel on Approved dialog (reviewer):

```
| Cancel ISS-... ?                   |
| It is approved but not posted yet. |
| Stock does not change. This can't  |
| be undone.                         |
| Reason  [____________________]     |   required here (optional for Draft and Submitted)
| [ Keep document ] [ Cancel document ]|
```

The Approve dialog also gets the PS-11 warning when the same shortage test is true (above the buttons, amber, not blocking): "QA-001 is short by 2,000 PCS right now. You can approve, but it can't be posted until stock arrives."

### 2.3 What the user sees afterwards

| After | Status chip | Toast | Banner or hint on the page | Focus |
|---|---|---|---|---|
| Recall (Edit) | Draft, no "Returned" badge | "Back to draft. Fix it, then submit again." | none | page heading, then the user moves to the line |
| Send back | creator: Draft + Returned badge; reviewer: Draft | reviewer: "Returned to {name} for changes." (existing) | creator banner: "Returned by {name}: {reason}" (existing, now also after an approval) | reviewer returns to the previous list |
| Cancel on Approved | Cancelled | "Document cancelled." (existing) | "Cancelled. Reason: {reason}" | heading |

Home: the creator's "Returned to me" and "My drafts" tiles count the sent-back document; a recalled one counts only in "My drafts". The reviewer's "Waiting for approval" does not change on send back or cancel (the document was Approved, not waiting), and drops by one when a Submitted document is recalled.

### 2.4 Rejected UI alternatives

- Back in the Post dialog recalls silently: "Back" would mean two things (close vs change state), and a user who only wants to look at the page would change the document.
- Show the Post dialog before submitting (state stays Draft on Back): `requiresApproval` is only known after Submit, so the dialog could promise a post that turns into an approval request, and it reopens the NV-01 and NV-02 half-saved failure modes.

## 3. Copy (keys in `web/libs/core/i18n`; `{name}`, `{docNo}`, `{sku}` as elsewhere)

zh and ms are drafts for review by a native speaker (NV-28 is still open for zh wording). Terms follow the existing files: zh 过账 / 驳回 / 批准 / 提交; ms Poskan / Tolak / Luluskan / Hantar / Sunting.

| Key | en | zh (draft) | ms (draft) |
|---|---|---|---|
| `doc.action.recall` | Edit | 编辑 | Sunting |
| `doc.action.sendBack` | Send back | 退回修改 | Pulangkan |
| `dialog.post.edit` | Edit items | 修改明细 | Sunting item |
| `dialog.recall.title` | Edit {docNo}? | 编辑 {docNo}？ | Sunting {docNo}? |
| `dialog.recall.body` | It leaves the approval list and goes back to draft. Submit it again when it is right. | 单据将离开审批列表并退回草稿。修改无误后请重新提交。 | Ia keluar daripada senarai kelulusan dan kembali menjadi draf. Hantar semula apabila betul. |
| `dialog.recall.keep` | Keep waiting | 继续等待 | Terus tunggu |
| `doc.toast.recalled` | Back to draft. Fix it, then submit again. | 已退回草稿。请修改后重新提交。 | Kembali kepada draf. Betulkan, kemudian hantar semula. |
| `doc.hint.recallWaiting` | Need to change something? Tap Edit. | 需要修改？请点“编辑”。 | Perlu ubah sesuatu? Ketik Sunting. |
| `doc.hint.recallReady` | Wrong quantity? Tap Edit. | 数量有误？请点“编辑”。 | Kuantiti salah? Ketik Sunting. |
| `doc.hint.approvedStaff` | A supervisor will post it. Wrong quantity? Ask {name} to send it back. | 将由主管过账。数量有误？请联系 {name} 退回。 | Penyelia akan memposkannya. Kuantiti salah? Minta {name} memulangkannya. |
| `dialog.sendBack.title` | Send {docNo} back? | 退回 {docNo}？ | Pulangkan {docNo}? |
| `dialog.sendBack.body` | Your approval is withdrawn. It goes back to {name} as a draft with your reason, and needs approval again. | 您的批准将被撤销。单据将作为草稿连同您的原因退回给 {name}，需要重新批准。 | Kelulusan anda ditarik balik. Ia kembali kepada {name} sebagai draf dengan sebab anda, dan perlu diluluskan semula. |
| `dialog.sendBack.keep` | Keep approved | 保留批准 | Kekalkan kelulusan |
| `dialog.cancelApproved.body` | It is approved but not posted yet. Stock does not change. This can't be undone. | 该单据已批准但尚未过账，库存不会变化。此操作无法撤销。 | Dokumen ini sudah diluluskan tetapi belum diposkan. Stok tidak berubah. Ini tidak boleh dibatalkan semula. |
| `doc.cannotPost.title` | Can't be posted now | 现在无法过账 | Tidak boleh diposkan sekarang |
| `doc.cannotPost.body` | {sku} needs {requested} {uom}, but only {available} are on hand in {warehouse}. Wait for stock, send it back, or cancel it. | {sku} 需要 {requested} {uom}，但 {warehouse} 仅有 {available}。请等待到货、退回修改或取消单据。 | {sku} memerlukan {requested} {uom}, tetapi hanya {available} ada di {warehouse}. Tunggu stok, pulangkan atau batalkan. |
| `dialog.approve.shortWarning` | {sku} is short by {short} {uom} right now. You can approve, but it can't be posted until stock arrives. | {sku} 目前缺 {short} {uom}。您可以批准，但到货前无法过账。 | {sku} kekurangan {short} {uom} sekarang. Anda boleh meluluskan, tetapi ia tidak boleh diposkan sehingga stok tiba. |
| `doc.hint.draftNotMine` | Draft by {name}. It is not waiting for approval. | {name} 创建的草稿，尚未提交审批。 | Draf oleh {name}. Ia tidak menunggu kelulusan. |
| `doc.stockChangedBodyEdit` (replaces `doc.stockChangedBodyCancel` for a reader who can recall) | Stock for {sku} is now {available} {uom}, but the document needs {requested}. Tap Edit to lower the quantity. | {sku} 目前库存为 {available} {uom}，但单据需要 {requested}。请点“编辑”减少数量。 | Stok {sku} kini {available} {uom}, tetapi dokumen memerlukan {requested}. Ketik Sunting untuk kurangkan kuantiti. |
| `doc.stockChangedBodyReviewer` (Approved, reader may send back or cancel) | Stock for {sku} is now {available} {uom}, but the document needs {requested}. Wait for stock, send it back, or cancel it. | {sku} 目前库存为 {available} {uom}，但单据需要 {requested}。请等待到货、退回修改或取消单据。 | Stok {sku} kini {available} {uom}, tetapi dokumen memerlukan {requested}. Tunggu stok, pulangkan atau batalkan. |

Reused unchanged: `doc.toast.cancelled`, the Returned banner, "Returned to {name} for changes.", E4 stale banner, "Enter a reason of at least 5 characters.".
Existing `doc.stockChangedBodyAsk` ("Ask a supervisor for help") is used only when the reader has none of Edit, Send back, Cancel.

Error copy (API problem text; the UI shows it in the existing inline alert):
- Recall on a document that is no longer Submitted, token current (only reachable with `If-Match: *`): "{docNo} is {status} and can't be taken back to draft."
- Send back or cancel by the wrong role: "Only an approver for this document can send it back." / "Only an approver for this document can cancel it after approval." (403)
- Reason too short: "Enter a reason of at least 5 characters." (400, existing)

## 4. Backend

All on `/api/documents/{id}`; `If-Match` required (428 if missing, 409 `stale` if the token is old, `*` is the explicit opt-out as today); response is the `DocumentDto` with its `ETag`; out-of-scope warehouse answers 404; audit row written in the same unit of work as the change. The `{id}` type follows the pending GUID-keys ADR (treat it as opaque).

| Endpoint | Body | Policy at the door | Domain / handler rule |
|---|---|---|---|
| `POST /api/documents/{id}/recall` | none | `DocumentCreate` | `StockDocument.Recall(userId, now)`; handler checks `DocumentActionPolicy.MayRecall` (status Submitted and `SubmittedBy == caller`). Audit `document.recalled`, before `{Status: Submitted}`, after `{Status: Draft}` |
| `POST /api/documents/{id}/send-back` | `{ "reason": string }` (required, trimmed length at least 5, max as other reasons) | `DocumentApprove` | `StockDocument.WithdrawApproval(reason, userId, now)`; handler checks status Approved, `MayReviewApproved`, and the same `ReviewRefusalAsync` as Reject. Audit `document.approval-withdrawn`, before `{Status: Approved, ApprovedBy}`, after `{Status: Draft, Reason}` |
| `POST /api/documents/{id}/cancel` (existing) | `{ "reason": string? }`; **required when the document is Approved** | new policy `DocumentCancel` = `DocumentCreate` or `DocumentApprove` (the Owner must reach it) | `Cancel` accepts Approved only for `MayReviewApproved`; Draft and Submitted rules unchanged |

Domain additions (names for be-framework to fit to style; one type per file, constants in `Ims.Domain/Common/Constants`):
- `StockDocument.Recall`, `StockDocument.WithdrawApproval`; `Cancel` extended with the Approved case and a mandatory reason there. The `Messages.OnlyDraftOrSubmittedCanCancel` text becomes "Only Draft, Submitted or Approved documents can be cancelled (current: {status})."
- `DocumentActions.Recall = "recall"`, `DocumentActions.SendBack = "sendBack"`; `RouteSegments.Recall`, `RouteSegments.SendBack`; `AuditActions.DocumentRecalled = "document.recalled"`, `AuditActions.DocumentApprovalWithdrawn = "document.approval-withdrawn"`, both added to `AuditActions.Known`.
- `DocumentActionPolicy.MayRecall(role, userId, doc)`: `Permissions.Has(role, DocumentCreate) && doc.Status == Submitted && doc.SubmittedBy == userId`.
- `DocumentActionPolicy.MayReviewApproved(role, doc)`: `doc.Status == Approved && Permissions.Has(role, DocumentApprove) && ApprovalPolicy.MayReview(role, doc.IsLarge)`. `MayCancel` returns it for Approved; `AllowedActions` uses it for Send back.
- No migration: Recall changes existing nullable columns; WithdrawApproval reuses `RejectionReason`, `RejectedBy`, `RejectedAt` and nulls `ApprovedBy`, `ApprovedAt`, `NegativeStockOverrideReason`.

Idempotent: a repeated call that would end in the state it already reached answers 200 with the current document and writes nothing (no second audit row), checked before the token comparison so a retry with the old token is not an error.
- recall: document already Draft and not Returned and caller is its creator or a Supervisor/Admin.
- send-back: document already Draft, Returned, and `RejectedBy == caller`.
- cancel stays as today (second call is 409 stale); the UI locks the button synchronously on first tap (NR-07 pattern).

`allowedActions` (the UI shows a button only if listed; order = primary first):

| Status, caller | Actions |
|---|---|
| Draft | unchanged: `edit`, `submit`, `cancel` |
| Submitted, approval needed, submitter (any role) | `recall`, `cancel` (+ `approve`, `reject` if they are also a permitted reviewer) |
| Submitted, approval needed, reviewer not submitter | `approve`, `reject`, `cancel` (Supervisor and Admin as today). No `recall` |
| Submitted, approval-free, submitter who may post | `post`, `recall`, `cancel`; Clerk: `recall`, `cancel` |
| Submitted, approval-free, others | unchanged: `post`, `cancel` for Supervisor and Admin; Staff `post` |
| Approved, reviewer who may post | `post`, `sendBack`, `cancel` |
| Approved, reviewer who may not post (Owner on Large) | `sendBack`, `cancel` |
| Approved, anyone else | none |
| Posted | `reverse` (unchanged) |
| Reversed, Cancelled | none |

List rows: `canAct` keeps its meaning (the caller can approve it now); it stays false for Approved rows. No change to the list DTO. `DocumentDto` needs no new field.

Counts and inbox (no code change expected, only tests): `ApprovalsWaiting` counts Submitted documents held for approval, so a recall removes the document from the inbox and the badge in the same request; `MyDrafts` counts Drafts of the creator, so it rises on recall; `ReturnedToMe` counts Drafts with a `RejectionReason`, so it rises on send back and not on recall. The inbox filter is unchanged. Approved documents never appear in the inbox (there is no "ready to post" list today; out of scope, see section 10).

## 5. Acceptance criteria

| ID | Criterion |
|---|---|
| DC-01 | A Staff user who submitted an approval-free document sees `[Edit] [Post]`; Edit returns it to Draft in one tap, no dialog, same number, same lines, no second document. |
| DC-02 | In the Post dialog the left button reads "Edit items" when the reader may recall, "Back" otherwise. "Edit items" recalls and opens the editor with the Items first. |
| DC-03 | Closing the Post dialog by Esc, outside tap or system Back changes nothing on the server and leaves the Submitted page with Edit next to Post. |
| DC-04 | A document waiting for approval: its submitter sees `[Edit]` as the only bar button; tapping it shows the Edit dialog; confirming leaves the approvals inbox and badge at once. |
| DC-05 | After recall the chip is Draft, there is no Returned badge, the toast is announced, and "My drafts" rises by one, "Returned to me" does not. |
| DC-06 | Submitting a recalled document again re-evaluates the approval rules (a changed quantity can change `requiresApproval`) and records a new submitter and time. |
| DC-07 | Recall by a user who is not the submitter is 403; recall of a Draft is a no-op 200; recall without `If-Match` is 428; with an old token is 409. |
| DC-08 | A Staff or Clerk user viewing an Approved document has no recall, no edit, no cancel; the page names the approver and says to ask them. |
| DC-09 | A reviewer sees Send back and Cancel document on an Approved document; Supervisor sees neither on a Large one (Owner and Admin do). |
| DC-10 | Send back requires a reason of 5+ characters (UI and API); afterwards the creator sees Draft, the Returned badge and the banner "Returned by {name}: {reason}"; `approvedBy` is empty; the audit has `document.approval-withdrawn`. |
| DC-11 | Cancel on Approved requires a reason; the document becomes Cancelled, stock and balances do not change, the audit has `document.cancelled` with before status Approved. |
| DC-12 | PR-02: with an approved issue short on stock and negative stock off, the Supervisor sees the "Can't be posted now" banner with Send back and Cancel document, and can cancel it. The Approve dialog showed the shortage warning (PS-11). |
| DC-13 | Posted is unchanged: no recall, send back or cancel is offered or accepted (400/403); Reverse works as before. |
| DC-14 | Staff never see a cost or value in any new dialog, banner or toast (EA-23). |
| DC-15 | Everything above fits at 360px in en, zh and ms, targets at least 48px, the sticky bar holds at most two buttons on one row (NR-03), focus is moved as in 2.3, axe finds no serious violation. |
| DC-16 | A double tap on Edit, Send back or Cancel sends one request. |

## 6. Playwright scenarios (new file `e2e/tests/30-document-corrections.spec.ts`; the number is a placeholder because plan v2 reserves 14 to 26, the behaviour-test-engineer picks the final one and adds a row to `e2e/README.md`)

Given/when/then titles, each named with the finding it protects, using the existing `api()`, `signIn`, `DocumentEditor` helpers (`editor.action('Edit')`, `editor.confirm(title, button, reason)`):

1. NR-02 / DC-01..03: staff saves a receipt with 110 instead of 10, Submit & post, in the dialog taps "Edit items"; the editor opens on the same number, the quantity is fixed to 10, Submit & post, Post; stock rose by 10 and exactly one document exists for that number.
2. NR-02 / DC-03: staff opens the Post dialog, presses Escape; the page is Submitted and shows Edit and Post; tapping Edit returns to Draft without a dialog.
3. DC-04, DC-05: staff submits an issue that needs approval; Edit dialog appears; confirm; the supervisor's inbox no longer lists it and the Home badge drops; the chip is Draft without Returned.
4. DC-06: after that recall the staff lowers the quantity below the threshold and submits; no approval is needed any more.
5. DC-07 (API): another user recalls (403), recall twice (second 200, one audit row), no `If-Match` (428).
6. Race / E4: supervisor approves first; staff taps Edit with the old page; the stale banner shows, Reload shows Approved and "Ask {name} to send it back"; no Edit button.
7. DC-10: supervisor approves a staff receipt, then Send back with reason "abc" (error sentence), then "Quantity wrong"; staff signs in: Returned badge, banner with the reason, edits, submits; supervisor approves again; Post.
8. DC-09: a Large document approved by the Owner: Supervisor sees no Send back or Cancel; Owner and Admin do.
9. PR-02 / DC-11, DC-12: negative stock off; two issues compete for the last units; the Approve dialog shows the shortage warning; approve; Post fails; the banner offers Send back and Cancel document; cancel with a reason; status Cancelled, stock unchanged, Home "Waiting" unchanged.
10. DC-13: on a Posted document none of Edit, Send back, Cancel document exists; Reverse does.
11. DC-14: for Staff, none of the new dialogs and banners contain a money-formatted number (reuse the helper of `13-cost-visibility`).
12. DC-15: at 360x740 in zh and ms the bar of Submitted and Approved documents is one row, no horizontal scroll; add the Submitted and Approved pages to the axe list in `11-accessibility`.

Existing specs to update: `01-receive` (the Back-in-dialog test now expects "Edit items" and the Edit button), `03-approval-override` (add the Approve-dialog shortage warning), any spec that asserts the Submitted page has only Cancel.

## 7. Slices (each under about 1,200 lines including tests)

| Slice | Role | Content | Est. lines | Needs |
|---|---|---|---|---|
| DC-BE1 | be-framework | Domain: `Recall`, `WithdrawApproval`, `Cancel` from Approved, policy methods, `allowedActions`, constants, messages, audit names; unit tests for the whole state table and the role matrix | 550 | none |
| DC-BE2 | be-framework | Application and API: two handlers, validators, `DocumentCancel` policy, routes, idempotent no-op, OpenAPI regeneration; integration tests (428, 409, 403, 404, audit rows, counts, inbox) | 700 | DC-BE1 |
| DC-FE1 | fe-framework | NR-02 path: `recall` in the document actions, Edit button and bar rules (2.1), Edit dialog, Post dialog button change, hints, recalled toast, en/zh/ms strings, component tests | 750 | DC-BE2 contract (NSwag) |
| DC-FE2 | fe-framework | Send back and Cancel-on-Approved dialogs with reason, shortage banner, Approve-dialog warning, `doc.hint.draftNotMine`, E5 copy variants, component tests | 650 | DC-FE1 |
| DC-E2E | behavior-test-engineer | Section 6 | 500 | DC-FE2 |

Order for the novice fix alone: BE1, BE2, FE1 closes NR-02. BE1, BE2, FE2 closes PR-02. Roughly 2 days for BE1+BE2, 2 days for FE1+FE2, 1 day for e2e.

## 8. Rejected alternatives (two lines each)

- Let the creator recall an Approved document too: it is one tap for the user, but one person could void another's decision without telling them; the cost of asking is one message and the audit is cleaner.
- Make Reject also work on Approved: no new endpoint, but the label "Reject" after approving is confusing, the audit could not tell the two events apart, and Owner/Supervisor routing needs its own wording.
- Add a status "Recalled" or "Withdrawn": one more state for every list, chip, filter and report for no new behaviour, since Draft already means "editable, not submitted".
- Edit as a client-only mode (edit the lines of a Submitted document and re-submit in one save): a document someone is reviewing would change under their hands; the Draft step makes that impossible.
- Only improve the label ("Not yet, edit") and keep Cancel as the fix: still forces retyping every line, which is the actual harm in NR-02.
- Cancel on Approved only when stock is short: a rule the user cannot see before trying, and a document that will never be posted for another reason (wrong customer, order withdrawn) would have no exit.

## 9. Follow-up edits to other docs (not done here, outside this task)

- `docs/design/mvp-ux.md`: 3.0 diagram and the line "Approved cannot (Q3)"; Q3 closed; 7.1 hint for Approved; 7.2 table rows Submitted/Approved and the rule list ("Cancel by the submitter stays allowed" gains Recall); 7.3 bar table; dialog table gains Edit, Send back, Cancel-on-Approved and the Post dialog button label; success toasts gain "Back to draft."; E5 copy.
- `docs/architecture/backend.md`: state machine sentence and the endpoint list (be-architect).
- `docs/design/2026-refresh-plan.md` slice R10 (action bar): the bar now may show Edit as primary.

## 10. For the product owner

These are business rules; each has the default used above, so the build does not wait.

1. May anyone but the approver undo an approval? Default: no. A Staff or Clerk user must ask the approver, who sends it back. (Alternative: the creator can pull back an Approved document with a reason.)
2. May the Owner send back or cancel an Approved Large document, although the Owner cannot post? Default: yes, Owner and Admin only for Large; Supervisor not.
3. Is a reason mandatory when cancelling an Approved document (5+ characters)? Default: yes, because stock-moving approvals are the audited step; Draft and Submitted cancel stay optional.

Not a decision, noted for later: a "Approved, ready to post" list for the posting supervisor does not exist; after Send back or Cancel nobody is notified except through the creator's Returned count. Notifications are slice A18.
