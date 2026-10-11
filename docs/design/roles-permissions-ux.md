# IMS roles and permissions - UX design

Author: ui-ux-designer. Date: 2026-10-11. Status: ready for fe-designer (look of the new components), fe-architect (routes, contracts, slices RP01..RP10) and be-architect (section 10 lists what the screens need from the model).
Builds on `docs/design/mvp-ux.md` (menu 1.2, cost rule 1.4, own-only 7.2, S50 Users and S52 Settings), `docs/design/2026-refresh-ux.md` and `2026-refresh-visual.md` (design language, density, tables, palette), `docs/design/2026-refresh-plan.md` (slices R/A/Q/L) and `docs/qa/retest-edge-accessibility.md` (ER-01, ER-10, ER-15).
Where this file and `mvp-ux.md` differ, this file wins for the items in section 11. No code is written here. Permission keys marked "(proposed)" are my names; the backend ADR `docs/architecture/adr-roles-permissions.md` was not in the repo when this was written and wins on names and rules (section 10 is the reconciliation list).

## 0. Summary of decisions

| # | Decision | Why (task speed or error prevention) |
|---|---|---|
| D1 | **Roles lives in the Administration group, not Master Data.** Menu order: Users, Roles, Settings, Audit log. It is gated by new permissions `roles.read` (see) and `roles.manage` (create, change, retire, delete). The Master Data permissions give no access to it. | Master data are business records that documents point at; roles decide who may see cost, post stock and administer. Mixing them would let a person who maintains products also edit who sees stock value. Admin tasks stay in one place. |
| D2 | One role per person (as today), plus optional **exceptions** (per-person grant or deny of single permissions) with a required reason and an optional expiry. | One role keeps "what can Siti do?" answerable in one glance. Exceptions cover the odd cases without inventing a new role per person. |
| D3 | **Warehouse scope is separate from permissions.** "What they can do" (role and exceptions) and "Where they can work" (warehouses) are two cards on the person's page. "Every warehouse" is a setting of the role, not a permission tick. | Users confuse "can post" with "can post in Store B". Two cards, two verbs, never mixed. |
| D4 | The role editor is a grouped checklist (six groups, 18 permissions), each with a plain-language sentence of what it allows, flags for "High impact" and "Sensitive", a tri-state group box, templates, a live **Changes** list and a live **Access preview** in sentences. One primary button, "Review and save", always opens a confirm step that lists the changes and the number of people affected. | People tick boxes from the name only if the consequence is visible. Seeing "Can see unit costs: Yes, stock value: No" before saving prevents the commonest mistake. |
| D5 | Safety rails are visible, not silent: a disabled control always shows its reason (the repo's `aria-disabled` + `aria-describedby` pattern). You cannot change your own access, cannot give what you do not hold, cannot touch the Admin role's permissions and cannot remove the last person who can manage roles and users. | Lockout and privilege escalation are the two ways this feature can hurt the business. Reasons in text tell the administrator what to do next instead of hiding the control. |
| D6 | A change reaches signed-in people within seconds. The server stops honouring old rights at once; the web app learns from a response header or a 30-second check and shows a polite, non-blocking banner "Your access was changed" with one button, [Refresh my access]. | Silent stale menus are confusing; a modal sign-out would destroy work. This keeps work and tells the truth. |
| D7 | Phone: every function works in one column (checklists, sheets, a bottom bar). Not supported on a phone: the side-by-side compare of three or more roles or people (it shows two at most, as a list of differences). Administration is not in the phone tab bar; it lives under More. | Rare, high-stakes work is done on a desk, but an urgent "take this right away" must also work from a phone. |
| D8 | System roles: Admin is fully locked. Owner, Supervisor, Warehouse staff and Clerk are **editable**, cannot be renamed, deleted or retired, show a "Changed from standard" marker and have a "Reset to standard" action. | Keeps the documented matrix (`mvp-ux.md` 1.1) as the seeded truth and gives a one-click way back after a mistake. |
| D9 | No Undo and no bulk "move all users" in this feature. A role with people cannot be deleted (it can be retired); people are moved one by one from their page. | Product rule "no Undo" (UX 7.4) and plan ruling N8 (no state-changing bulk). Retiring is reversible; deleting is not. |
| D10 | Everything shown follows the existing cost rule: the Roles screens show permission names, never amounts; a person without `audit.read` still sees a "History" link only if they can open it. | FR-07. |

Owner-only questions with my defaults are in section 9.2.

## 1. Where it lives in the navigation

### 1.1 Answer to "Master Data should be able to add role"

I read the phrase as "the place where administrators maintain the system should be able to add roles", not as "the people who hold a Master Data role". Roles sits under **Administration**, and the **permission `roles.manage`** gates it. Reasons:

1. Master data (products, categories, warehouses, partners) is data the documents refer to. Roles and permissions are security configuration. They have the same sensitivity as Users, which is already in the Admin group.
2. The menu follows permission, never role names (`mvp-ux.md` 1.2, UX 11.10). If the owner later wants Supervisors to add roles, the owner ticks `roles.manage` on the Supervisor role. No menu change is needed.
3. A person who may edit products (`masterdata.manage`) should not be able to decide who sees stock value.

If the owner meant the persona, the answer is the same with a different default: grant `roles.manage` to that role (owner question Q1).

Shortcut so nobody needs to hunt: the Master Data group does not get a Roles item, but the command palette finds "Roles" and the Users screen has a "Manage roles" quiet link next to its page title (shown only with `roles.read`).

### 1.2 Menu and routes

| Menu group (label) | Item | Route | Shown when (any of) | Notes |
|---|---|---|---|---|
| Administration | Users | `/users` | `users.read`, `users.manage` | existing, now also `users.read` (read-only for people without manage) |
| Administration | Roles | `/roles` | `roles.read`, `roles.manage` | new, icon `shield` |
| Administration | Settings | `/settings` | `settings.manage` | existing |
| Administration | Audit log | `/audit-log` | `audit.read` | existing |

- The group label changes from "Admin" to "Administration" (`nav.group.admin`; zh 管理, ms Pentadbiran stay).
- New routes: `/roles` (list), `/roles/compare` (side by side), `/roles/:id` (editor), `/users/:id` (a person's page with tabs Access, Profile, Activity), `/users/compare?a=&b=`. Ids are opaque strings (GUID decision pending).
- Guards: `requirePermission(roles.read)` on all `/roles*`; `requirePermission(users.read)` on `/users*`. A person with the read permission but not the manage permission sees every screen read-only with a "View only" chip (visual spec 3.15) and no write buttons (absent, not disabled).
- Phone: Administration items are not in the bottom tab bar (the tabs derive from permissions, section 10 item 2); they are listed in More and found by the palette.

### 1.3 Who may do what (default holders after the migration)

| Capability | Permission | Admin | Owner | Supervisor | Staff | Clerk |
|---|---|---|---|---|---|---|
| See users | `users.read` | yes | no | no | no | no |
| Add, edit, turn off users, set role and warehouses, reset password | `users.manage` | yes | no | no | no | no |
| See roles and what they allow | `roles.read` | yes | no | no | no | no |
| Add, change, retire, delete roles | `roles.manage` | yes | no | no | no | no |
| Give or remove single permissions for one person (exceptions) | `users.overrides` (proposed) | yes | no | no | no | no |
| Read the audit log | `audit.read` | yes | yes | no | no | no |

Every row is only the seeded default. The owner can change it from this feature (and the guards in section 5 stop anyone locking everyone out).

### 1.4 Screen inventory (new and changed)

| ID | Screen | Route | Priority | Device | Notes |
|---|---|---|---|---|---|
| S53 | Roles list | `/roles` | Must | D | table, create dialog, row menu |
| S54 | Role editor | `/roles/:id` | Must | D | permission checklist, side panel (preview, people, changes) |
| S55 | Add role dialog | over S53 | Must | D | name, description, start from |
| S56 | Compare roles | `/roles/compare` | Should | D | read-only matrix, up to 5 roles |
| S50 | Users list (changed) | `/users` | Must | D | adds Role, Exceptions columns; create stays a dialog; row opens S57 |
| S57 | Person's page | `/users/:id` | Must | D | tabs Access, Profile, Activity |
| S58 | Add exception dialog | over S57 | Should | D | grant or deny, reason, expiry |
| S59 | Compare people | `/users/compare` | Could | D | person vs role, person vs person |

## 2. The Roles list (S53)

### 2.1 Behaviour

1. Columns, in order of importance (desktop): Role (name, description on a second line), Type (chip), People, Permissions ("14 of 18"), Modified (date, by), Status. Phone card: name + type chip on line 1; "4 people - 14 of 18 permissions" on line 2.
2. Type chip: **Standard** (icon `lock`, label "Standard") for the five system roles, **Custom** (icon `pencil`) for the rest. Admin also shows "Always full access" as secondary text. The word is "Standard" in the UI (plain) and "system" in the code.
3. Status: **Active** or **Retired** (a retired role cannot be given to anyone new; people who already have it keep it; the row shows "Retired - 3 people still have it" with a warning icon).
4. A "Changed" flag chip appears on a standard role whose permissions differ from the standard set ("Changed from standard", icon `pencil`).
5. A flag chip "Admin rights" (warning outline, icon `shield`) when the role holds any High impact permission. Reason: the supervisor scanning the list sees where power sits.
6. Search (name or description, 100 chars), filters: Type (All, Standard, Custom), Status (Active, Retired, All). Default: Active. Default sort: Standard first in menu order (Admin, Owner, Supervisor, Warehouse staff, Clerk), then custom roles by name. Sort buttons on Role, People, Modified.
7. Row: the whole row opens the editor (the name is the link). One inline control only: the row menu `...` (44px on touch) with **Copy as new role**, **Retire** / **Reactivate**, **Delete**, each present only if allowed. Disabled-with-reason items for the cases in 2.3.
8. Primary action: [+ Add role] (needs `roles.manage`). Secondary: [Compare roles] (Should).
9. Counts: "People" is the number of active plus turned-off accounts holding the role, shown as `5` with a sr-only suffix "people". Clicking the number is not a target; the editor's People tab and `Users` filtered by role are the routes (the link "Show people" in the row menu opens `/users?role=<id>`).
10. Created is shown in the editor header ("Created 02 Oct 2026 by Raj"); the list shows Modified, because administrators scan for what changed lately. (The owner asked for both: the list has a "Created" column that is hidden by default under A03 column choice and `priority 3`.)

### 2.2 Create, copy, start from

[+ Add role] opens a dialog (a bottom sheet on phone):

```
+--------------------------------------------------+
| Add a role                                   [X] |
| * means required                                 |
| Role name *        [ Night-shift clerk         ] |
| Description        [ Receives and issues at    ] |
|                    [ night                     ] |
| Start from                                       |
|  ( ) Blank - nothing allowed                     |
|  (o) A standard template   [ Clerk          v ]  |
|  ( ) A role I already have [ Choose a role  v ]  |
|                                                  |
| The role is created now. You choose its          |
| permissions on the next screen.                  |
|                                                  |
|                         [ Cancel ] [ Add role ]  |
+--------------------------------------------------+
```

- Start from is one control group: Blank, Standard template (a select: the five standard sets plus "Read-only viewer"), Existing role (a select of all active roles with their current settings). Default: Standard template, none preselected (the Add button stays enabled; Blank is chosen if nothing is picked and the line below the group says "Nothing is allowed until you tick permissions.").
- [Add role] creates the role at once (so it has an address and a history), copies the chosen permissions and warehouse setting, and opens the editor with the banner "Started from {source}. Review the permissions, then save." (info, status role). No role gets assigned to anyone by being created, so there is no risk in creating before saving.
- Name rules: required, 2 to 40 characters, unique ignoring case and accents; description up to 200. A duplicate shows on the field: "A role named \"{name}\" already exists." (409 mapped to the field, ER-04).
- Copy as new role (row menu or editor menu) opens this dialog with the name prefilled "{name} (copy)" and "A role I already have" preselected to the source.

### 2.3 Retire and delete

| Case | Row menu | Dialog |
|---|---|---|
| Custom role, no people | Retire, Delete | Retire: "Retire {name}?" "It won't be offered when you assign a role. You can reactivate it later." [Back] [Retire]. Delete: "Delete {name}?" "This can't be undone. The audit trail keeps its history." [Keep role] [Delete role] (danger) |
| Custom role, people > 0 | Retire (allowed), Delete disabled with reason | Retire body adds: "{count, plural, one {1 person keeps} other {# people keep}} this role until you move them." Delete disabled: "{count, plural, one {1 person has} other {# people have}} this role. Move them to another role first." + link "Show people" |
| Standard role (not Admin) | Reset to standard (if changed), no Retire, no Delete | menu items absent; the editor says why in one line: "Standard roles can't be deleted or retired." |
| Admin | none | "The Admin role always has every permission and can't be changed." |
| Last role that keeps the system administrable | Retire and Delete disabled | see 5.4 |

### 2.4 States and copy

| State | Treatment | Copy |
|---|---|---|
| Loading | table skeleton (rows with name bar, chip, three short bars) | sr-only "Loading roles" |
| Empty (no custom roles, filter Custom) | `app-empty-state` | "No custom roles yet. The five standard roles cover most teams." [Add role] |
| Empty (search) | | "No role matches \"{query}\"." [Clear search] |
| Error | error panel with Retry (one announcement) | "Couldn't load roles. Try again." |
| View only | chip "View only" next to the title; no [Add role], no row menu | |

### 2.5 Wireframes

Desktop (1280):

```
+---------------------------------------------------------------------------------------------------------+
| IMS      | Roles                                                         [ Compare roles ] [ + Add role ] |
|----------+----------------------------------------------------------------------------------------------|
| Home     | [ Search roles...            ]   Type: All <v>   Status: Active <v>                            |
| ...      |----------------------------------------------------------------------------------------------|
| ADMINIS- | Role                          Type          People  Permissions  Modified               Status   |
| TRATION  |----------------------------------------------------------------------------------------------|
| Users    | Admin                         [STANDARD]       2      18 of 18   --                     Active   |
| Roles <  |   Always full access                                                                         |
| Settings | Owner                         [STANDARD]       1       5 of 18   --                     Active   |
| Audit log| Supervisor                    [STANDARD] [CHANGED]  4  15 of 18  09 Oct 2026 Raj      Active   |
|          |   Approves, posts, reverses and manages master data                                          |
|          | Warehouse staff               [STANDARD]       9       6 of 18   --                     Active   |
|          | Clerk                         [STANDARD]       3       7 of 18   --                     Active   |
|          | Night-shift clerk             [CUSTOM]         2       7 of 18   10 Oct 2026 Raj    [...]  Active|
|          | Auditor (read only)           [CUSTOM] [RETIRED] 1     6 of 18   02 Oct 2026 Raj    [...]  Retired|
|          |----------------------------------------------------------------------------------------------|
|          | 1-7 of 7                                                                                     |
+---------------------------------------------------------------------------------------------------------+
```

Phone (360):

```
+------------------------------------+
| Roles                       (user) |
+------------------------------------+
| [ Search roles...                ] |
| Type <v>   Status <v>              |
+------------------------------------+
| Supervisor      [STANDARD][CHANGED]|
| 4 people - 15 of 18 permissions    |
+------------------------------------+
| Night-shift clerk       [CUSTOM]   |
| 2 people - 7 of 18 permissions     |
+------------------------------------+
|                         [ + Add ]  |
+------------------------------------+
|Home|Receipts|Issues|Stock|More     |
+------------------------------------+
```

## 3. The role editor (S54)

### 3.1 Page structure

Order top to bottom (DOM order = visual order):

1. Page header: breadcrumb "Roles > {name}", title (the role name), chips (Standard / Custom, Active / Retired, View only), the primary [Review and save] (shows the number of changes, disabled with the reason "No changes yet" while clean) and a quiet [Discard changes] (only while dirty), and the `...` menu (Copy as new role, Reset to standard, Retire / Reactivate, Delete, View history).
2. A one-line status row: "{count, plural, one {1 person has} other {# people have}} this role. Changes reach them within a minute." and "Last changed 09 Oct 2026 14:05 by Raj - View history".
3. Banners when relevant: started from a source; Admin locked; stale (E4-style); retired; "You hold this role" (section 5.2).
4. Details card: Role name (required), Description, **Warehouse access** radio group ("Only the warehouses assigned to each person" / "Every warehouse"), Approval level (only shown when Approve is ticked, section 3.6). The legend "* means required" sits above the first field.
5. Permissions: the grouped checklist (3.2) with the group index.
6. Side panel (desktop >= 1280 docked at the right, 24rem; below that it becomes tabs above the checklist): **Access preview**, **People**, **Changes**.

### 3.2 The permission checklist

Six groups in this order, each a `<fieldset>` with a visible `<legend>` that is also an `h2`: **Documents**, **Stock**, **Master data**, **Reports**, **Cost and valuation**, **Administration**. (Cost and valuation sits before Administration so that the sensitive pair is seen by everyone who edits a role; Administration is last because it is the most dangerous and least often changed.)

Group header row: tri-state group checkbox (`aria-checked="mixed"` when some are on) labelled "Documents", a count "2 of 4 on", and a quiet button [Clear] (turns the whole group off). The group checkbox is the bulk select.

Permission row, left to right:

| Part | Content |
|---|---|
| Checkbox | 24px glyph in a 44px target (56px row on touch); the whole row toggles; the label is the permission name |
| Text | Name (weight 500), then the "what it allows" sentence (14px muted, always visible, never a tooltip) |
| Flags | `High impact` (warning outline, icon `shield`) and/or `Sensitive` (neutral outline, icon `eye`) as text chips; "Requires: {names}" in muted text when it has dependencies |
| Change marker | when the row differs from the saved role: a 3px accent bar on the inline start, a `pencil` icon and the word "Changed" (muted, 13px); sr-only "Changed: was off, now on" |
| Lock reason | when the control is disabled: `lock` icon and the reason in text ("You can't give a permission you don't have.") |

Rules:

1. **Bulk select skips High impact permissions.** Ticking a group box turns on every permission in the group that is not High impact and not disabled; a polite message says so ("Turned on 3. 1 high-impact permission must be turned on by itself."). The box stays mixed. Clearing a group turns everything off, High impact included. Reason: one stray tick must not grant the power to change roles.
2. **Dependencies are followed, not blocked.** Turning a permission on also turns on what it needs, and says so in the live region and in a notice under the row ("Also turned on: See stock and documents, because Post documents needs it."). Turning a required permission off turns its dependants off with the same notice. The catalog supplies `requires[]`; the server re-checks.
3. **Disabled controls always show a reason in text**: you do not hold it (escalation), the role is Admin (locked), you have view only, your own role removal would lock you out. They stay focusable (`aria-disabled`), with the reason linked by `aria-describedby`.
4. **Unknown permission key** from a newer server: row shows the key, the sentence "This permission was added in a newer version and has no description in this app. Refresh the app." and the checkbox works. Never hide it.
5. Group order and permission order come from the catalog (`group`, `sort`), the words come from the i18n catalog (`perm.<key>.name`, `.help`), so zh and ms are covered like any text.
6. Warehouse scope is **not** in the checklist (D3).

Group index: on desktop a short "Jump to" list of the six groups above the checklist (links; `nav` landmark "Permission groups"); on phone a select "Jump to group". Focus moves to the group heading.

### 3.3 The catalog (names, help text and flags)

Single source for the i18n keys `perm.<key>.name`, `perm.<key>.help`, `perm.<key>.can`, `perm.<key>.cannot`. "Req." is the dependency list. Keys marked (proposed) do not exist yet.

| Group | Key | Name | What it allows (help text) | Flag | Req. | Preview: allowed | Preview: not allowed |
|---|---|---|---|---|---|---|---|
| Documents | `document.create` | Create receipts and issues | Start receipts and issues, edit their own drafts and submit them. | | `stock.read` | Can create and submit receipts and issues. | Cannot create receipts or issues. |
| Documents | `document.approve` | Approve or return documents | Approve or return documents that need approval, up to their approval level. Cannot approve a document they submitted unless Settings allows it. | | `stock.read` | Can approve or return documents ({level}). | Cannot approve documents. |
| Documents | `document.post` | Post documents | Post a receipt or issue so stock changes. A role without approval rights can post only documents that do not need approval. | | `stock.read` | Can post documents. | Cannot post documents. |
| Documents | `document.reverse` | Reverse posted documents | Undo a posted document by adding opposite stock entries. A reason is required. | High impact | `document.post` | Can reverse posted documents. | Cannot reverse posted documents. |
| Stock | `stock.read` | See stock and documents | See stock levels, low stock, the stock ledger and receipts and issues in the warehouses they work in. | | | Can see stock and documents. | Cannot see stock or documents. |
| Master data | `masterdata.read` | See products, warehouses and partners | Open the lists of products, categories, units, warehouses and partners. | | | Can see master data. | Cannot see master data. |
| Master data | `masterdata.manage` | Edit master data | Add and change products, categories, units, warehouses and partners; deactivate them; import products and opening stock. | | `masterdata.read` | Can change master data. | Cannot change master data. |
| Master data | `partners.create` | Add partners | Add a new supplier or customer. Cannot change or deactivate existing partners. | | `masterdata.read` | Can add new partners. | Cannot add partners. |
| Reports | `reports.read` | Open reports and download Excel files | Use Reports and download Excel files. Cost columns appear only with "See unit costs", value columns only with "See stock value". | | `stock.read` | Can open reports and download Excel files. | Cannot open reports. |
| Cost and valuation | `cost.view` | See unit costs and document amounts | See the unit cost and line cost on documents, document totals and approval amounts. | Sensitive | | Can see unit costs. | Cannot see unit costs. |
| Cost and valuation | `valuation.view` | See average cost and stock value | See average cost, stock value and the stock total. | Sensitive | `stock.read` | Can see stock value. | Cannot see stock value. |
| Administration | `users.read` | See the user list | See who has an account, their role and warehouses. | | | Can see the user list. | Cannot see the user list. |
| Administration | `users.manage` | Manage users | Add and edit people, turn accounts off or on, reset passwords, set their role and warehouses. | High impact | `users.read`, `roles.read` | Can manage users. | Cannot manage users. |
| Administration | `users.overrides` (proposed) | Give or remove single permissions for a person | Add exceptions to one person's access: allow or take away a single permission, with a reason. | High impact | `users.read`, `roles.read` | Can set exceptions for a person. | Cannot set exceptions. |
| Administration | `roles.read` (proposed) | See roles and permissions | Open the Roles screens and see what each role allows. | | | Can see roles and permissions. | Cannot see roles. |
| Administration | `roles.manage` (proposed) | Create and change roles | Add roles, change what each role allows, retire or delete roles. | High impact | `roles.read` | Can create and change roles. | Cannot change roles. |
| Administration | `settings.manage` | Change settings and approval rules | Change negative stock, self-approval, alert timing and the approval rules. | High impact | | Can change settings and approval rules. | Cannot change settings. |
| Administration | `audit.read` | Read the audit trail | See who changed what and when. | Sensitive | | Can read the audit trail. | Cannot read the audit trail. |

Impact rule: **High impact** = changes who may do what, changes system rules, or undoes stock (`document.reverse`, `users.manage`, `users.overrides`, `roles.manage`, `settings.manage`). **Sensitive** = shows private figures or records (`cost.view`, `valuation.view`, `audit.read`). Everything else is normal. Both flags always carry a word, never colour alone.

### 3.4 Presets ("start from Supervisor")

- Create dialog (2.2) covers new roles. In an existing role, the menu item **Apply a template...** opens a small dialog with the same Start from group (Standard template or Existing role) and the line "This replaces the ticks below. Nothing is saved until you review and save." [Back] [Apply]. The result is an ordinary set of unsaved changes, so Changes shows the diff and [Discard changes] returns to the saved state.
- Standard templates are the shipped sets of the five standard roles (and "Read-only viewer", a Could) and are listed with a one-line summary in the select: "Supervisor - approves, posts, reverses, manages master data, sees cost and value".
- A standard role has **Reset to standard** (menu): same as applying its own standard template.
- Applying a template to a role never changes warehouse access unless the template says so; the dialog states "Warehouse access: {value}" before Apply.

### 3.5 Changes (the diff of unsaved edits)

Tab "Changes (n)" in the side panel (n = number of changed items; a badge with the number and the text). Also shown in the review dialog.

```
Changes (3)                                [ Discard all ]
Turned on
  + See stock value            Cost and valuation  [Sensitive]   [Undo this]
  + Reverse posted documents   Documents           [High impact] [Undo this]
Turned off
  - Edit master data           Master data                       [Undo this]
Details
  ~ Warehouse access: Only assigned warehouses -> Every warehouse [Undo this]
```

- Rows are grouped Turned on / Turned off / Details (name, description, warehouse access, approval level). Each row is a sentence with an icon (`plus`, `minus`, `pencil`) and text; the per-row [Undo this] is a button (the repo's "no Undo" rule is about saved data; this reverts an unsaved tick).
- Empty: "No changes yet." (muted).
- Order within a section follows the checklist order.
- The tab name announces "Changes, 3" and a polite live message "3 changes" updates after a 500 ms pause.

### 3.6 Approval level (only when Approve is ticked)

Approval rules today name a required role (`roleRequired`: Supervisor, Owner, Admin) and the server ranks roles. A custom role cannot be ranked by name, so the editor shows, directly under the Approve row, a select **Approval level**: "Supervisor level - approves documents below the large limit" or "Owner level - approves large documents". The Admin role stays "every document". This control exists only if be-architect keeps rank-based approval (section 10 item 1); if the ADR replaces `roleRequired` with a permission or a role id, this control is replaced by whatever the ADR defines and the Settings rule form uses the same role picker.

### 3.7 People with this role

Tab "People (7)" in the side panel.

- Header text: "{total} people ({active} active, {off} turned off)".
- A compact table (name, username, status chip, warehouses count, "Exceptions" chip when the person has any), 10 rows, then "See all {total} in Users" (opens `/users?role=<id>`). Each row links to the person's page (accessible name "Open access for {name}").
- Empty: "No one has this role yet." and, with `users.manage`, a link "Give this role to someone" opening Users.
- Phone: cards, two lines (name + status; username + warehouses).
- Loading: three skeleton rows. Error: "Couldn't load the people. Try again." with Retry (a failure here does not block editing).
- Reading this list needs `users.read`; without it the tab shows only the count and "You don't have access to the list of people."

### 3.8 Access preview ("effective access")

Tab "Access preview" in the side panel; the default tab. It reads the **unsaved** ticks, so it always answers "what will this role be able to do if I save?". It is computed in the browser from the permission set by one pure function; the sentence table is 3.3.

Top block, "Can a person with this role...?", ten rows with an icon and the words Yes or No (never colour alone):

| # | Row label | Yes when |
|---|---|---|
| 1 | Create and submit receipts and issues | `document.create` |
| 2 | Approve documents | `document.approve` (adds "up to {level}") |
| 3 | Post documents | `document.post` |
| 4 | Reverse posted documents | `document.reverse` |
| 5 | See unit costs | `cost.view` |
| 6 | See stock value | `valuation.view` |
| 7 | Change products, warehouses and partners | `masterdata.manage`; shows "Add partners only" when only `partners.create` |
| 8 | Open reports and download Excel files | `reports.read` |
| 9 | Manage users, roles or settings | any of `users.manage`, `roles.manage`, `settings.manage` (lists which) |
| 10 | Work in every warehouse | the role's warehouse access |

Below it, three lines of consequence:

- "Menu: Home, Receipts, Issues, Stock, Low stock, Products, ..." (the menu items the permission set would show; uses the same filter as the app, so it cannot drift).
- "Home screen: shortcuts to receive and issue stock" / "...: a list of documents waiting for approval" / "...: stock figures only" (the Home profile of UX 2.2, derived from the same permissions).
- "Phone: bottom tabs Home, Receipts, Issues, Stock" (from the permission-derived tab list).

Then "Full list": a disclosure "Show everything this role can and cannot do ({n} and {m})" with the two sentence lists from the table in 3.3 (allowed sentences first). Consistency warnings (status role, warning style) appear above the rows when the set is odd: "This role can approve documents but cannot see stock." (cannot happen if dependencies are followed, kept as a guard for server-sent sets), "No one with this role can open any screen." (empty set).

Preview wireframe (side panel):

```
+-------------------------------------+
| [Access preview] [People 7] [Changes 3] |
| A person with this role...          |
|  Create receipts and issues   Yes   |
|  Approve documents            No    |
|  Post documents               Yes   |
|  Reverse posted documents     No    |
|  See unit costs               Yes   |
|  See stock value              No    |
|  Change master data           Add partners only |
|  Open reports and download    No    |
|  Manage users, roles, settings No   |
|  Work in every warehouse      No    |
|                                     |
| Menu: Home, Receipts, Issues,       |
|   Stock, Low stock, Products,       |
|   Warehouses, Partners              |
| Home: shortcuts to receive and      |
|   issue stock                       |
| v Show everything (9 can, 9 cannot) |
+-------------------------------------+
```

### 3.9 Full desktop wireframe

```
+--------------------------------------------------------------------------------------------------------+
| IMS      | Roles > Night-shift clerk  [CUSTOM] [ACTIVE]               [Discard changes] [Review and save (3)]|
|          | 2 people have this role. Changes reach them within a minute. Last changed 10 Oct 14:05 by Raj  |
|          |                                                              View history  [...]               |
|----------+------------------------------------------------------------------+-----------------------------|
| Menu     | * means required                                                  | [Access preview][People 2]  |
|          | Role name *   [ Night-shift clerk               ]                 | [Changes 3]                 |
|          | Description   [ Receives and issues at night    ]                 |                             |
|          | Warehouse access  (o) Only the warehouses assigned to each person | A person with this role...  |
|          |                   ( ) Every warehouse                             |  Create receipts...    Yes  |
|          |-------------------------------------------------------------------|  Approve documents     No   |
|          | Permissions    Jump to: Documents Stock Master data Reports ...   |  See unit costs        Yes  |
|          |                                                                   |  See stock value       No   |
|          | [-] Documents                                  2 of 4 on  [Clear] |  ...                        |
|          |   [x] Create receipts and issues                                  |                             |
|          |       Start receipts and issues, edit their own drafts and ...    |                             |
|          |   [ ] Approve or return documents                                 |                             |
|          |       Approve or return documents that need approval, up to ...   |                             |
|          |   [x] Post documents                                              |                             |
|          |   [ ] Reverse posted documents       [HIGH IMPACT]                |                             |
|          |                                                                   |                             |
|          | [x] Stock                                      1 of 1 on  [Clear] |                             |
|          |   [x] See stock and documents                                     |                             |
|          | [-] Master data ...                                               |                             |
|          | [ ] Cost and valuation                         1 of 2 on  [Clear] |                             |
|          |   |[x] See unit costs and document amounts   [SENSITIVE]  CHANGED|                             |
|          |   [ ] See average cost and stock value       [SENSITIVE]          |                             |
|          | [ ] Administration  (every row shows: You can't give a permission you don't have.) |           |
+--------------------------------------------------------------------------------------------------------+
```

### 3.10 Phone layout of the editor

```
+------------------------------------+
| <  Night-shift clerk       [CUSTOM]|
+------------------------------------+
| 2 people. Changes apply in a minute|
| Role name *   [ Night-shift clerk ]|
| Description   [                   ]|
| Warehouse access                   |
|  (o) Only assigned warehouses      |
|  ( ) Every warehouse               |
| [Access preview] [People] [Changes]|   <- tabs, panel opens below
+------------------------------------+
| Jump to group:  Documents  <v>     |
| Documents               2 of 4 on  |
| [x] Create receipts and issues     |
|     Start receipts and issues, ... |
| [ ] Approve or return documents    |
|     ...                            |
+------------------------------------+
| 3 changes     [ Review and save ]  |   <- sticky bar above the tab bar
+------------------------------------+
|Home|Receipts|Issues|Stock|More     |
+------------------------------------+
```

Rows are 56px minimum; the description wraps fully (no clamp). At 320 px and 400 % zoom the bar and tab bar follow the existing in-flow rule (EA-04); nothing sticky covers a focused checkbox.

### 3.11 Review and save dialog

Opens from [Review and save]. It always appears (one extra step on a rare, high-impact action).

```
+----------------------------------------------------+
| Save changes to "Night-shift clerk"?          [X]  |
| 2 people have this role. They get the changes      |
| within a minute.                                   |
|                                                    |
| Turned on                                          |
|  + See stock value  [Sensitive]                    |
|  + Reverse posted documents  [High impact]         |
| Turned off                                         |
|  - Edit master data                                |
|                                                    |
| (!) High-impact permission: someone with this role |
|     will be able to undo posted documents.         |
| Reason for the change *   [                      ] |
|     (kept in the audit trail)                      |
|                                                    |
|                    [ Back ]  [ Save changes ]      |
+----------------------------------------------------+
```

- The dialog calls the dry-run (section 10 item 4) before opening and shows its result: affected people, anything blocked (5.4), whether you are affected (5.2).
- **Reason** is required (5 or more characters after trimming) only when the diff turns on a High impact permission or changes warehouse access to Every warehouse; otherwise it is an optional "Note for the audit trail".
- The danger warning sentence uses the permission's `can` sentence: "someone with this role will be able to {can-sentence-without-Can}".
- Buttons: [Back] [Save changes]. Save is a plain primary, not the danger style (the change is reversible by editing again); the warning block carries the weight. For a change that removes a permission from a role that people use, the dialog adds "{count} people will lose: {names}" in the same block.
- On success: the dialog closes, focus returns to [Review and save] (now disabled with "No changes yet"), and one toast says "Saved. {count, plural, one {1 person} other {# people}} will see the new access within a minute." The toast is the single confirmation channel because the save button sits inside a dialog that has just closed (the in-button Saved state of UX 7.2 would never be seen).
- On failure: message inside the dialog above the buttons (`role="alert"`), the dialog stays open, the typed reason stays (PS-01 rule). Stale role: banner on the page and the dialog says "{name} changed this role at {time}. Reload to see the latest version. Your changes were not saved." [Reload].

### 3.12 Special roles

- **Admin**: the whole page is read-only. Banner (info): "The Admin role always has every permission, so the system can never be locked. It can't be changed." All 18 boxes shown checked and disabled (the reason is the banner, linked by `aria-describedby` on the fieldsets). The People and Access preview tabs work.
- **Standard roles (Owner, Supervisor, Warehouse staff, Clerk)**: name and description are read-only text (not disabled inputs); the permissions are editable; the "Changed from standard" chip appears when different. Menu: Reset to standard (when changed), Copy as new role, View history.
- **Retired role**: banner (warning, status) "This role is retired. It isn't offered when you assign a role. {count} people still have it." with [Reactivate].
- **View only** (`roles.read` without `roles.manage`): the checklist renders as a read-only list: Allowed (check icon + text) / Not allowed (minus icon + text), no checkboxes, no save, chip "View only".

### 3.13 Loading, empty and error states

| State | Treatment |
|---|---|
| Loading | header skeleton, three group skeletons (heading bar + four rows of a box, a bar and a short bar). Side panel skeleton. The page is not interactive until the role and the catalog both loaded |
| Catalog failed | "Couldn't load the list of permissions. Try again." with Retry; the editor does not open with an empty list (a role must never be saved from a partial catalog) |
| Role not found | S03 "Page not found" (also for a role the user may not read; 404 not 403) |
| Save conflict | stale banner as above |
| Offline | the page loads read-only from nothing; Review and save is blocked with "You're offline. Reconnect to save." (no queued command, plan N9) |

## 4. The person's access page (S57)

Route `/users/:id`. The Users list row opens it (the existing edit dialog is replaced by this page; **creating** a person stays a dialog because it is a single short task: username, name, email, password, role, warehouses, as today; after creation the toast has a "View access" action).

### 4.1 Page structure

Header: back link "Users", name, username, chips (Active / Turned off, role chip, "Has exceptions (2)"), `...` menu (Compare with..., Reset password, Turn off account / Turn on account, View audit trail). Tabs: **Access** (default), **Profile**, **Activity**. The tab strip is a real tablist with panels (ER-18).

**Access tab** has four cards in this order:

1. **What they can do** (role and exceptions)
2. **Where they can work** (warehouses)
3. **Exceptions** (section 4.4)
4. **Effective permissions** (section 4.5)

DOM order = visual order. Each card has its own save, so one card's error cannot block another: **What they can do** saves the role (button [Save role]), **Where they can work** saves warehouses ([Save warehouses]). Exceptions save immediately per exception through their dialog (add, remove).

**Profile tab**: username, display name, email, password reset (write-only, as today), Active switch (turn off/on). This is the old dialog's content. Turning an account off goes through the confirm "Turn off {name}?" "{name} is signed out within a minute and can't sign in. Their documents stay." [Back] [Turn off].

**Activity tab**: the last 20 audit entries about this person's access (role changes, exceptions added or removed, warehouses, sign-in lockouts), newest first, each a sentence (section 5.8) and the link "Open in Audit log" (pre-filtered to this person). Needs `audit.read` for the link; without it the tab shows only "Last changed 10 Oct 2026 by Raj".

### 4.2 Card 1: What they can do (role)

- Field: **Role** (select of active roles, a retired role still shows if the person has it, marked "Retired"). Under it a one-line summary of the chosen role: "Supervisor - 15 of 18 permissions, 4 people" with a link "See what this role allows" (opens the role in a side drawer, A16 pattern, read-only preview; for people without `roles.read` the link is absent and the summary shows only the sentences of the preview).
- Options the signed-in administrator may not assign (escalation, 5.3) are shown but `aria-disabled` with the secondary text "Has rights you don't have".
- When the chosen role differs from the saved one, a live **Difference line** appears under the field before saving: "Changing the role from Clerk to Supervisor: turns on 8 permissions, turns off 1. Exceptions stay." with the details in a disclosure "Show the 9 differences". This is the compare view (4.6) in miniature.
- [Save role] opens a confirm only when the change adds a High impact permission or lowers rights of a person currently signed in ("Raj is signed in. The change reaches them within a minute."); otherwise it saves directly (a routine change by a person who can manage users does not need two steps).
- Own page: the field is read-only with the line "You can't change your own access. Ask another administrator." (5.1).

### 4.3 Card 2: Where they can work (warehouses)

- Heading "Where they can work", hint "This is separate from what they can do. A person can only work in the warehouses ticked here."
- If the role has "Every warehouse": the checkboxes are replaced by the sentence "{role} can work in every warehouse. Warehouses aren't picked for this role." (status role, muted) and the list is hidden, so there is no dead control.
- Otherwise: a checkbox per warehouse (the existing control), a count "2 of 3 selected", and when none is ticked the warning (status role, `alert` icon): "Without a warehouse this person can sign in but can't work with stock."
- [Save warehouses]. Saved state on the button, one polite announcement.

### 4.4 Card 3: Exceptions

Purpose: a single permission differs from the role for this person. Always visible, never hidden behind "More" (they change what a person can do).

- Banner above the cards when any exception exists (info, status role): "{count, plural, one {1 exception changes} other {# exceptions change}} what {name} can do compared with the {role} role." Also visible on the list (the Exceptions chip) and in the header.
- Table: **Permission**, **Effect**, **Reason**, **Added** (date, by), **Expires**, and one row action. Effect is a chip with text: "Allowed by exception" (accent outline, icon `plus`) or "Taken away by exception" (warning outline, icon `minus`). Expires shows a date or "No expiry"; within 7 days it adds the word "Soon" with an `clock` icon. Row action: [Remove] (accessible name "Remove exception: {permission} for {name}"), which confirms with "Remove this exception? {name} will go back to what the {role} role allows for {permission}." [Back] [Remove exception].
- Expired exceptions are not in effect. They move to a collapsed section "Expired exceptions ({n})" (disclosure, closed by default) with the same columns minus the action; history stays in the audit trail.
- [+ Add exception] (needs `users.overrides`; absent otherwise; disabled on your own page with the reason in 5.1).
- Empty: "No exceptions. {name} has exactly what the {role} role allows." [Add exception].
- Phone: cards with Permission as heading, effect chip, reason (full), "Expires 30 Oct 2026", [Remove].

**Add exception dialog** (S58; bottom sheet on phone):

```
+--------------------------------------------------------+
| Add an exception for Siti Rahman                  [X]  |
| * means required                                       |
| What should change *                                   |
|  (o) Give a permission Siti's role doesn't have        |
|  ( ) Take away a permission Siti's role has            |
| Permission *   [ Choose a permission             v ]   |
|    (grouped list; options that don't apply are shown   |
|     disabled with the reason: "Siti's role already     |
|     allows this." / "Siti's role doesn't allow this.") |
| What this does:                                        |
|    Siti will be able to see stock value although the   |
|    Supervisor role doesn't allow it.      [Sensitive]  |
| Reason *       [                                    ]  |
|                (kept in the audit trail, 5-200 chars)  |
| Expires        (o) No expiry ( ) 7 days ( ) 30 days    |
|                ( ) Pick a date [ 20 Oct 2026 ]         |
|                                                        |
|                          [ Cancel ] [ Add exception ]  |
+--------------------------------------------------------+
```

- Radio decides which permissions the select lists (Give: those the role lacks and the administrator holds; Take away: those the role has). This makes a pointless exception impossible.
- "What this does" is a live sentence from the catalog (`can` / `cannot`) with the person's first name; it is the preview for exceptions and appears before saving.
- Dependencies: giving a permission that needs another one the person lacks adds a line "This also needs: {names}. Add them as exceptions too, or choose a role that has them." and disables [Add exception] with that reason (no hidden auto-adds for a single person, because the effect on audit would be surprising).
- Reason is always required, 5 to 200 characters (the API enforces it too). Expiry is optional; the quick choices set a date at 23:59 Kuala Lumpur time; a picked date must be after today ("Choose a date after today."). The dialog never sets a default expiry, but the owner may want one (Q5).
- High impact permission given by exception: after [Add exception] a confirm step appears in the same dialog (a second screen, not another dialog): "Give Siti a high-impact permission?" "Siti will be able to {can sentence}." [Back] [Give permission].
- Success: toast "Exception added. Siti will see it within a minute." and the new row gets the 1.5-second row highlight.

### 4.5 Card 4: Effective permissions (where each one comes from)

A read-only list of the same six groups, but each row says **Allowed** or **Not allowed** and **where it comes from**. This is the answer to "why can Siti see stock value?".

| Column | Content |
|---|---|
| Permission | name, with the flags |
| Result | "Allowed" (icon `check`) / "Not allowed" (icon `minus`) |
| Source | "Role: Supervisor" / "Exception: allowed by Raj on 02 Oct 2026 - \"Covering the stocktake\" - until 20 Oct 2026" / "Exception: taken away by Raj ..." / "Not given" (muted) |

- Filter chips above: **All**, **Allowed**, **From exceptions** (the default is All; a person with exceptions sees the count on the chip). The filter is a toggle group (`aria-pressed`), not a tab strip.
- Rows from exceptions get the change marker (accent bar + icon + text), so the difference from the role is findable without colour.
- Search box "Find a permission" (client-side).
- Phone: one card per permission row, grouped under the group headings.
- Headline answers on top (same ten Yes/No rows as the role preview, computed from the person's effective set), so a supervisor can read "Can see stock value: Yes, from an exception until 20 Oct" at a glance.

### 4.6 Compare

Entry points: the `...` menu **Compare with...** on a person (offers "Their role without exceptions" or "Another person...") and **Compare roles** on the Roles list.

- One component, `app-access-compare`, with 2 to 5 columns (people or roles) and rows = permissions grouped as in the checklist. Cell = Allowed / Not allowed with icon + text; for people a short source tag ("role", "exception"). A first control **Differences only** (default on) hides rows where every column agrees; with it off the full matrix shows. The page states the count: "12 differences in 18 permissions".
- **Person vs their role**: two columns (person, role), the exceptions are the differences by definition. This view is also the Difference line of 4.2 and the Exceptions-only filter of 4.5.
- **Person vs person**: pick the second person with a combobox (users with `users.read`); also lists warehouses on one extra line "Warehouses: Main WH, Store B | Main WH".
- **Role vs role** (`/roles/compare?ids=a,b,c`): up to 5 roles; this is the table that explains the standard matrix of `mvp-ux.md` 1.1 and the owner's "who can do what".
- Keyboard and screen reader: the compare table is an ARIA grid (A04 pattern): row headers are the permission names (`th scope="row"`), column headers the subjects; one Tab stop, arrows move; each cell's name reads "{Permission}, {subject}: Allowed, from exception". Group headings are rows with `colspan`. See 6.2.
- Phone: at most two subjects, shown as one list of the differences ("Approve documents: Allowed for Siti, Not allowed for Mei"); 3 or more shows "Comparing three or more works best on a larger screen." and falls back to the first two. Not supported on a phone: the full matrix.

### 4.7 Wireframe: person's Access tab (desktop)

```
+--------------------------------------------------------------------------------------------------------+
| IMS      | Users > Siti Rahman  [ACTIVE] [Supervisor] [EXCEPTIONS 1]              [...]                  |
|          | [ Access ] [ Profile ] [ Activity ]                                                           |
|----------+-----------------------------------------------------------------------------------------------|
|          | (i) 1 exception changes what Siti can do compared with the Supervisor role.                  |
|          |                                                                                               |
|          | What they can do                                                                              |
|          |  Role *      [ Supervisor                v ]    Supervisor - 15 of 18 permissions            |
|          |                                                  See what this role allows                    |
|          |                                                                       [ Save role ]           |
|          |-----------------------------------------------------------------------------------------------|
|          | Where they can work                                                                           |
|          |  Separate from what they can do. Only the ticked warehouses.                                  |
|          |  [x] Main WH   [x] Store B   [ ] Store C                                      2 of 3 selected |
|          |                                                                     [ Save warehouses ]       |
|          |-----------------------------------------------------------------------------------------------|
|          | Exceptions                                                           [ + Add exception ]      |
|          |  Permission           Effect                  Reason              Added           Expires     |
|          |  See stock value      [ALLOWED BY EXCEPTION]  Covering stocktake  02 Oct Raj      20 Oct  [Remove]|
|          |  v Expired exceptions (0)                                                                      |
|          |-----------------------------------------------------------------------------------------------|
|          | Effective permissions        [ All ] [ Allowed ] [ From exceptions (1) ]   [ Find...        ]  |
|          |  Documents                                                                                    |
|          |   Create receipts and issues     Allowed       Role: Supervisor                               |
|          |   Reverse posted documents       Allowed       Role: Supervisor                               |
|          |  Cost and valuation                                                                           |
|          |   See stock value        |CHANGED| Allowed      Exception: allowed by Raj until 20 Oct         |
+--------------------------------------------------------------------------------------------------------+
```

### 4.8 Users list changes (S50)

- New columns: **Role** (name, "Retired" suffix if applicable) and **Access** (chip "Exceptions (2)" when present; a person with a turned-off account shows the status chip). Filters: Role (from `GET /api/roles`, replaces the hard-coded list), Has exceptions (toggle).
- Row link opens `/users/:id`. Row actions follow ER-10: the only inline action is the link, named "Open access for {name}".
- The Role select in the create dialog lists active roles from the API, with the same "Has rights you don't have" disabled options.

## 5. Safety UX

### 5.1 Protections against removing your own ability to administer

| ID | Situation | Behaviour | Copy |
|---|---|---|---|
| SF-01 | Administrator opens their own person page | Role, warehouses and exceptions are read-only; Turn off account is absent; the effective list works | "You can't change your own access. Ask another administrator." (status banner, linked to the disabled controls) |
| SF-02 | Administrator edits a role they hold and the change removes a permission they need to administer (`roles.manage`, `roles.read`, `users.manage`, `users.read`, or anything from the Administration group they currently rely on) | The checkbox can still be unticked, the Review dialog shows the block, [Save changes] is `aria-disabled` | "This would remove your own access to {permission}. Another administrator can make this change." |
| SF-03 | The role being edited is the only source of an Administration permission for the signed-in administrator, and the change removes any other permission | Allowed; the review dialog adds a line | "You have this role. These changes apply to you too." |
| SF-04 | Administrator tries to turn off their own account | Absent on their own page; if reached by URL, 403 | "You can't turn off your own account." |

### 5.2 Last-admin guard

The server keeps the rule "at least one active person must hold every Administration permission that is needed to repair access" (`users.manage`, `roles.manage`, `users.read`, `roles.read`). The web app asks the dry-run before the confirm dialog (section 10 item 4) so the message appears before the user commits, and also handles a `409 last_administrator` from the actual call.

| ID | Trigger | Copy |
|---|---|---|
| SF-05 | Turning off an account | "{name} is the only active person who can manage users and roles. Give another person that access before you turn this account off." |
| SF-06 | Changing a person's role (or removing it) | "{name} is the only active person who can manage users and roles. Keep a role with that access, or give it to another person first." |
| SF-07 | Removing a permission from a role | "If you save this, nobody could {manage roles}. Keep this permission, or give it to another role first." (one sentence per affected permission) |
| SF-08 | Retiring or deleting a role | "This is the only role that lets someone manage {users and roles}. Give that access to another role first." |
| SF-09 | Adding a deny exception that would remove the last holder | "This would leave nobody able to {manage roles}." |

Each message sits where the action is (inside the review dialog or beside the control), is a `role="alert"` once, offers one next step as a link or button ("Show who has this access" opens Users filtered), and never uses the person's password or email.

### 5.3 Privilege escalation

| ID | Rule | UX |
|---|---|---|
| SF-10 | You cannot give a permission you do not hold (to a role, or by exception). | The box is shown but `aria-disabled`, with a `lock` icon and "You can't give a permission you don't have." Already-ticked permissions you do not hold stay as they are; you may untick them. |
| SF-11 | You cannot assign a role that holds permissions you do not hold. | Role option disabled, secondary text "Has rights you don't have". |
| SF-12 | You cannot edit the Admin role or give a person Admin unless you are Admin. | Admin editor read-only; the option is disabled with the same text. |
| SF-13 | You cannot change the access of a person who holds more than you do (server rule; open point for be-architect). | If the server refuses: "You can't change access for someone with more access than you." |

### 5.4 Confirmation wording for dangerous changes

Dialogs follow `app-confirm-dialog`: title is a question with the thing named, body states the consequence in the person's terms, the confirm button is a verb, the danger style is used only for irreversible actions, and the confirm button is never pre-focused on a destructive dialog.

| Action | Title | Body | Buttons | Reason |
|---|---|---|---|---|
| Save a role change (3.11) | Save changes to "{name}"? | people count, the diff, warnings | [Back] [Save changes] | required for High impact |
| Give a High impact permission to a role | (inside the save dialog) | "Someone with this role will be able to {action}." | | required |
| Give a High impact permission by exception | Give {name} a high-impact permission? | "{name} will be able to {action}." | [Back] [Give permission] | the exception reason |
| Remove a permission people use | (inside the save dialog) | "{count} people will lose: {names}." | | optional |
| Retire a role | Retire "{name}"? | "It won't be offered when you assign a role. {count, plural, one {1 person keeps} other {# people keep}} it until you move them." | [Back] [Retire] | optional |
| Delete a role | Delete "{name}"? | "This can't be undone. The audit trail keeps its history." | [Keep role] [Delete role] (danger) | |
| Reset a standard role | Reset "{name}" to standard? | "This turns {on} permissions on and {off} off. {count} people have this role." | [Back] [Reset role] | optional |
| Change a person's role | (inline difference line, confirm only for High impact or a signed-in person) | | [Back] [Save role] | |
| Remove an exception | Remove this exception? | "{name} will go back to what the {role} role allows for {permission}." | [Back] [Remove exception] | |
| Turn off an account | Turn off {name}? | "{name} is signed out within a minute and can't sign in. Their documents stay." | [Back] [Turn off] (danger) | |

### 5.5 When rights change for people who are signed in

Goal: they hear about it within seconds, never lose work, and the server never trusts old rights (D6).

| Moment | What happens | Copy / behaviour |
|---|---|---|
| Server | Every response carries the person's permission version (`X-Permissions-Version`); a request made with an outdated version is refused with `403 permissions_changed` (or accepted with the old rights only for read-only calls if the ADR keeps a short window, ER-15; the banner works either way) | |
| Web app, any API call | The first response whose version differs raises the banner | |
| Web app, idle | A check of `GET /api/me/permissions-version` every 30 seconds while the tab is visible, once on tab focus | within 30 s worst case, on the next click 0 s |
| Banner | Top of the page content (below the top bar, above the page header), status role (`role="status"`), does not take focus, stays until used; one at a time | "Your access was changed by an administrator. Refresh to use your new access." [Refresh my access] |
| [Refresh my access] | Re-reads the permission set (token refresh), rebuilds the menu, Home and guards without reloading the page; if the current page is no longer allowed, goes to the Forbidden page with the sentence below; unsaved forms are kept until the person leaves them (R26 rule) | toast: "Your access is up to date." |
| Action no longer allowed | The call fails with 403; one inline banner at the place of the action (no toast), form kept | "You no longer have permission to {action}. Your access changed at {time}." |
| Page no longer allowed | S03 | "Your access changed. You can't open this any more. Ask an administrator if you need it." [Go to Home] |
| Account turned off | Next call is `401` with `account_disabled`; the re-sign-in dialog (R26) shows the sentence; no draft survives for another user | "This account has been turned off. Ask an administrator." |
| Role retired or deleted | Retired: nothing changes for the person. Deleted is impossible while people hold it | |
| The administrator | After saving, the toast states the count and "within a minute" so the administrator knows what to tell people | "Saved. 2 people will see the new access within a minute." |

Accessibility: the banner is announced politely once; the button is the first control inside it; the 30-second poll never moves focus or scrolls. Motion: the banner appears without animation (same as UX 7.3 inline messages).

### 5.6 Unsaved-changes guard

- Same guard as documents (`unsaved-changes.guard`), dialog "Discard changes?" with an explanation specific to this page: "You changed {count, plural, one {1 setting} other {# settings}} of \"{name}\". They are not saved." [Keep editing] [Discard]. The person's page guards only the role select (the other cards save by themselves, so they have nothing unsaved after their save).
- `beforeunload` is set while a role is dirty.
- Opening the command palette does not trigger the guard (UX CP-13); running a navigation from it does.
- A session that ends while a role is dirty follows R26: re-sign-in in place, the ticks are kept in memory, nothing is written to browser storage.

### 5.7 Audit trail link

- Role editor: "Last changed ... by ... View history" (link) and menu item View history; Person: Activity tab and menu item. Both open `/audit-log` with the entity filter set (Entity = Role or User, id, date range open). Without `audit.read` the links are absent and the text "Last changed ... by ..." remains.
- Every write in this feature is audited by the server (role created, renamed, retired, reactivated, deleted; permission turned on or off with the reason; warehouse access changed; person's role changed; exception added, removed, expired; account turned off or on). Needs: audit entity filter on Role and User, and a `reason` field in the entry (section 10 item 8).

### 5.8 Audit sentences (what the Activity tab and Audit log row say)

| Event | Sentence |
|---|---|
| Role created | "Raj added the role \"Night-shift clerk\" (started from Clerk)." |
| Permission on | "Raj turned on \"See stock value\" for the role \"Night-shift clerk\". Reason: {reason}" |
| Permission off | "Raj turned off \"Edit master data\" for the role ..." |
| Role retired / reactivated / deleted | "Raj retired the role ..." |
| Person's role changed | "Raj changed Siti Rahman's role from Clerk to Supervisor." |
| Warehouses changed | "Raj changed Siti Rahman's warehouses: added Store B, removed Store C." |
| Exception added | "Raj gave Siti Rahman \"See stock value\" as an exception until 20 Oct 2026. Reason: {reason}" |
| Exception removed / expired | "Raj removed the exception ..." / "The exception ... expired." |
| Account off / on | "Raj turned off Siti Rahman's account." |

## 6. Phone, accessibility, responsive rules, density, themes

### 6.1 Phone behaviour (< 600 px)

| Area | On a phone |
|---|---|
| Navigation | Administration items under More; palette finds them; no tab bar slot |
| Roles list | cards (2.5), search, filters in a "Filters" sheet; Add role as a sheet |
| Role editor | full function in one column; side panel is three tabs below the details card; sticky bar "n changes - Review and save" above the tab bar; group "Jump to" select; rows 56 px; description always visible |
| Review and save, dialogs | bottom sheets, buttons side by side, reason field 48 px, keyboard-aware (`interactive-widget=resizes-content`) |
| Person's page | tabs scroll horizontally if needed with a visible cue; cards stack; exceptions as cards; effective permissions as grouped cards |
| Add exception | bottom sheet; the permission select is a full-height listbox sheet |
| Compare | two subjects at most, as a list of differences; three or more says "Comparing three or more works best on a larger screen." |
| Not supported | the side-by-side matrix (3 to 5 columns), column choice and resize, dragging |
| Fast paths that must stay one or two taps | open a person (1 tap from Users), Turn off account (menu, confirm), Remove an exception (button, confirm) - urgent security actions |

Touch targets 44 px (48 px primary), no hover-only help, no fixed bar over a focused control, no horizontal page scroll at 320 px or 400 % zoom (FR-02, FR-03).

### 6.2 Accessibility of the large lists and matrix

1. **Structure**: each group is a `fieldset` with a `legend`; the legend text is also the group heading (`h2`); one `h1` per page. Screen reader users move by heading and by form control. Heading order: page `h1`, group `h2`, no skips.
2. **Checkbox naming**: name = permission name; `aria-describedby` = the help sentence + flag words + the change marker + the lock reason (in that order). Example read-out: "See average cost and stock value, checkbox, not checked. See average cost, stock value and the stock total. Sensitive. Changed."
3. **Tri-state group box**: `aria-checked="mixed"`, name "Documents, 2 of 4 selected", Space toggles per the bulk rule; the result is announced ("Turned on 3. 1 high-impact permission must be turned on by itself.").
4. **Live regions**: one polite region for the page: dependency notices, bulk results, change counts (debounced 500 ms), "Saved". Errors use `role="alert"` once. No chatter on every tick beyond the dependency notice.
5. **No colour-only meaning**: allowed / not allowed = check / minus icon + the words; changed = bar + icon + "Changed"; flags = icon + words; exception effect = icon + words; disabled = reason text. Forced colors: borders on chips and rows stay (visual spec "border: 1px solid transparent").
6. **Keyboard (checklist)**: Tab / Shift+Tab move between controls (group box, [Clear], each checkbox); Space toggles; there are no single-key shortcuts here. A "Skip to people and preview" link is not needed because the side panel follows the details card in tab order on narrow layouts and comes after the checklist on wide layouts (DOM order = visual order: on desktop the panel is docked after the checklist in the DOM and positioned by grid, never by CSS `order`).
7. **Keyboard (compare matrix and effective list as a table)**: the grid pattern of A04: the table is one Tab stop, arrows move between cells, Home/End and Ctrl+Home/End, Page Up/Down; Enter on a source tag opens nothing (no hover-only data: the source is written in the cell). Group rows are `role="row"` with a single `columnheader`-like cell and `aria-label` "Group: Documents".
8. **Focus**: after Review and save closes, focus returns to the opener; after a bulk action focus stays on the group box; after [Undo this] focus moves to the next change row or, if none, to [Review and save]'s page heading; after adding an exception focus goes to the new row's [Remove] button, announced "Exception added".
9. **Errors**: field errors under the field with `aria-describedby`; a banner "{n} things to fix" when several; first invalid field focused on submit (ER-04 fix applied here from the start); 409 duplicate name mapped to the Name field.
10. **Reduced motion, zoom, text size**: no animation is required for meaning (row highlight is a tint held 1.5 s); text may be scaled to 200 % without clipping; descriptions wrap (`overflow-wrap: anywhere` for role names, which are user text).
11. **Role names are user text**: they can contain emoji, RTL marks and long words; trim, collapse spaces, reject control characters (server too); display with `overflow-wrap: anywhere`, never ellipsis in the editor header.

### 6.3 Responsive rules

| Width band (plan L01) | Roles list | Role editor | Person's page |
|---|---|---|---|
| Phone < 600 | cards | one column, panel as tabs, sticky bar | tabs, stacked cards |
| Tablet 600-1023 | table without Created and Modified (priority 3 hidden under the disclosure) | one column, panel as tabs above the checklist | two-column details row, cards stacked |
| Desktop 1024-1279 | full table | one column plus panel as tabs | cards in a single column, tables full |
| Roomy >= 1280 | full table | checklist (flex) + docked 24 rem panel | same, tables full |

Density (comfortable / compact, desktop only): rows of the Roles list, the exceptions table, the effective list and the compare matrix follow it; the checklist rows keep two lines of text, so compact only trims padding (44 px rows, never below 36). The checklist and the dialogs ignore density's font change (16 px text kept).

### 6.4 Light and dark

No new colours. Chips use the status flag styles (outline) so contrast is already proven: High impact = `warning` outline with `shield`; Sensitive = neutral outline (`border-strong`, `text`) with `eye`; Allowed by exception = `accent` outline; Taken away = `warning` outline; Changed marker = `highlight` row tint (2026-refresh) with the accent bar; Standard chip = neutral with `lock`. The `danger` colour is not used for permission flags (it is reserved for errors, destructive buttons and the asterisk, UX 3.2). New icons for fe-designer: `shield`, `eye`, `lock` (also in the login slice), `plus`, `minus`, `pencil` (exists), `clock` (exists). Contrast of each pair is added to `check-contrast.mjs` by the slice that adds the component.

## 7. Copy

en is the source of truth; zh and ms are drafts to be reviewed by a native speaker (marked `TODO-review` in the catalog). ICU plural syntax is used (`{count, plural, ...}`), no sentence is built by concatenation. Placeholders: `{name}` person or role, `{permission}` permission name, `{count}`, `{role}`, `{time}`.

### 7.1 Main labels

| Key | en | zh (draft) | ms (draft) |
|---|---|---|---|
| nav.group.admin | Administration | 管理 | Pentadbiran |
| nav.roles | Roles | 角色 | Peranan |
| roles.title | Roles | 角色 | Peranan |
| roles.add | Add role | 添加角色 | Tambah peranan |
| roles.copy | Copy as new role | 复制为新角色 | Salin sebagai peranan baharu |
| roles.compare | Compare roles | 比较角色 | Bandingkan peranan |
| roles.type.standard | Standard | 标准 | Standard |
| roles.type.custom | Custom | 自定义 | Tersuai |
| roles.status.retired | Retired | 已停用 | Dihentikan |
| roles.changedFromStandard | Changed from standard | 已修改（与标准不同） | Berubah daripada standard |
| roles.adminRights | Admin rights | 管理权限 | Hak pentadbir |
| roles.people | People | 人数 | Orang |
| roles.permissions | Permissions | 权限 | Kebenaran |
| roles.modified | Modified | 修改时间 | Diubah |
| roles.startFrom | Start from | 起始模板 | Mula daripada |
| roles.startFrom.blank | Blank - nothing allowed | 空白 - 不允许任何操作 | Kosong - tiada yang dibenarkan |
| roles.startFrom.template | A standard template | 标准模板 | Templat standard |
| roles.startFrom.role | A role I already have | 已有的角色 | Peranan yang sudah ada |
| roles.warehouseAccess | Warehouse access | 仓库范围 | Akses gudang |
| roles.warehouseAccess.assigned | Only the warehouses assigned to each person | 仅限分配给每个人的仓库 | Hanya gudang yang ditetapkan kepada setiap orang |
| roles.warehouseAccess.all | Every warehouse | 所有仓库 | Semua gudang |
| roles.reviewAndSave | Review and save | 查看并保存 | Semak dan simpan |
| roles.discard | Discard changes | 放弃更改 | Buang perubahan |
| roles.tab.preview | Access preview | 权限预览 | Pratonton akses |
| roles.tab.people | People | 人员 | Orang |
| roles.tab.changes | Changes | 更改 | Perubahan |
| roles.preview.heading | A person with this role... | 拥有此角色的人… | Orang yang ada peranan ini… |
| roles.yes / roles.no | Yes / No | 可以 / 不可以 | Ya / Tidak |
| perm.group.documents | Documents | 单据 | Dokumen |
| perm.group.stock | Stock | 库存 | Stok |
| perm.group.master | Master data | 基础数据 | Data induk |
| perm.group.reports | Reports | 报表 | Laporan |
| perm.group.cost | Cost and valuation | 成本与估值 | Kos dan penilaian |
| perm.group.admin | Administration | 管理 | Pentadbiran |
| perm.flag.high | High impact | 高影响 | Impak tinggi |
| perm.flag.sensitive | Sensitive | 敏感 | Sensitif |
| perm.changed | Changed | 已更改 | Diubah |
| perm.allowed / perm.notAllowed | Allowed / Not allowed | 允许 / 不允许 | Dibenarkan / Tidak dibenarkan |
| user.access | Access | 权限 | Akses |
| user.whatCanDo | What they can do | 可执行的操作 | Apa yang boleh dilakukan |
| user.whereWork | Where they can work | 可工作的仓库 | Di mana boleh bekerja |
| user.exceptions | Exceptions | 例外 | Pengecualian |
| user.exception.add | Add exception | 添加例外 | Tambah pengecualian |
| user.exception.give | Give a permission the person's role doesn't have | 授予其角色没有的权限 | Beri kebenaran yang tiada pada peranannya |
| user.exception.take | Take away a permission the role has | 收回角色拥有的权限 | Tarik balik kebenaran yang ada pada peranan |
| user.exception.allowed | Allowed by exception | 例外允许 | Dibenarkan melalui pengecualian |
| user.exception.denied | Taken away by exception | 例外收回 | Ditarik balik melalui pengecualian |
| user.exception.reason | Reason | 原因 | Sebab |
| user.exception.expires | Expires | 到期 | Tamat tempoh |
| user.exception.noExpiry | No expiry | 无期限 | Tiada tarikh tamat |
| user.effective | Effective permissions | 实际权限 | Kebenaran berkesan |
| user.source.role | Role: {role} | 角色：{role} | Peranan: {role} |
| user.compare | Compare with... | 与…比较 | Bandingkan dengan… |
| user.differencesOnly | Differences only | 仅显示差异 | Perbezaan sahaja |
| rights.changed.banner | Your access was changed by an administrator. Refresh to use your new access. | 管理员已更改您的权限。请刷新以使用新权限。 | Akses anda telah diubah oleh pentadbir. Muat semula untuk menggunakan akses baharu. |
| rights.refresh | Refresh my access | 刷新我的权限 | Muat semula akses saya |

### 7.2 Messages and errors (en)

| ID | Copy |
|---|---|
| RP-M01 role name duplicate | A role named "{name}" already exists. |
| RP-M02 role name length | Enter a name of 2 to 40 characters. |
| RP-M03 admin locked | The Admin role always has every permission, so the system can never be locked. It can't be changed. |
| RP-M04 standard not deletable | Standard roles can't be deleted or retired. |
| RP-M05 role in use | {count, plural, one {1 person has} other {# people have}} this role. Move them to another role first. |
| RP-M06 started from | Started from {source}. Review the permissions, then save. |
| RP-M07 dependency on | Also turned on: {names}, because {permission} needs {them}. |
| RP-M08 dependency off | Also turned off: {names}, because they need {permission}. |
| RP-M09 bulk skipped | Turned on {count}. {skipped, plural, one {1 high-impact permission} other {# high-impact permissions}} must be turned on by itself. |
| RP-M10 escalation | You can't give a permission you don't have. |
| RP-M11 role option escalation | Has rights you don't have |
| RP-M12 own access | You can't change your own access. Ask another administrator. |
| RP-M13 own roles removal | This would remove your own access to {permission}. Another administrator can make this change. |
| RP-M14 you hold this role | You have this role. These changes apply to you too. |
| RP-M15 last admin | {name} is the only active person who can manage users and roles. Give another person that access first. |
| RP-M16 no admin left | If you save this, nobody could {action}. Keep this permission, or give it to another role first. |
| RP-M17 exception redundant | {name}'s role already allows this. / {name}'s role doesn't allow this. |
| RP-M18 exception dependency | This also needs: {names}. Add them as exceptions too, or choose a role that has them. |
| RP-M19 exception date | Choose a date after today. |
| RP-M20 reason | Enter a reason of at least 5 characters. (existing text) |
| RP-M21 stale role | {name} changed this role at {time}. Reload to see the latest version. Your changes were not saved. |
| RP-M22 saved | Saved. {count, plural, one {1 person} other {# people}} will see the new access within a minute. |
| RP-M23 no longer allowed | You no longer have permission to {action}. Your access changed at {time}. |
| RP-M24 page no longer allowed | Your access changed. You can't open this any more. Ask an administrator if you need it. |
| RP-M25 unknown permission | This permission was added in a newer version and has no description in this app. Refresh the app. |
| RP-M26 no warehouse | Without a warehouse this person can sign in but can't work with stock. |
| RP-M27 role empty set | No one with this role can open any screen. |
| RP-M28 retired role | This role is retired. It isn't offered when you assign a role. {count} people still have it. |
| RP-M29 view only | View only |
| RP-M30 offline | You're offline. Reconnect to save. |

## 8. Acceptance criteria and Playwright scenarios

### 8.1 Acceptance criteria

IDs are the tester's list. Every one holds in light and dark, at 360 px and 1280 px, and for the English, Chinese and Malay catalogs unless stated.

**Navigation and gates (RPN)**

| ID | Criterion |
|---|---|
| RPN-01 | Admin sees Administration: Users, Roles, Settings, Audit log in that order; Owner sees only Audit log; Staff, Clerk, Supervisor see none |
| RPN-02 | A custom role holding only `roles.read` shows Roles in the menu, every screen read-only with "View only" and no write control |
| RPN-03 | `/roles` without `roles.read` shows the Forbidden page; the Master Data permissions alone never show Roles |
| RPN-04 | The palette lists Roles for a person with `roles.read` and not for others |
| RPN-05 | On a phone the Administration items are under More and the tab bar still has four tabs derived from permissions |

**Roles list (RPL)**

| ID | Criterion |
|---|---|
| RPL-01 | Five standard roles are marked Standard with a lock icon and text; Admin has no row menu; the others have no Delete or Retire |
| RPL-02 | People and Permissions counts are correct after assigning a person and after saving a role |
| RPL-03 | Add role with Start from "Clerk" creates a role whose ticks equal the Clerk set; the editor shows the "Started from" banner; nobody gets the role |
| RPL-04 | A duplicate name (any case) shows the error on the Name field, focus moves to it, no toast |
| RPL-05 | Delete is disabled with the reason and a "Show people" link while the role has people; Retire works and the row shows "Retired - n people still have it" |
| RPL-06 | Search, Type and Status filters work together; empty states match the table in 2.4 |
| RPL-07 | Every row action has a unique accessible name (ER-10) |

**Role editor (RPE)**

| ID | Criterion |
|---|---|
| RPE-01 | The checklist shows six groups with a legend/heading each, and every permission shows its help sentence without hover |
| RPE-02 | High impact and Sensitive flags show icon and text on the right permissions; a screen reader hears them in the description |
| RPE-03 | The group box is tri-state; bulk select skips High impact permissions and says so; Clear turns the group off |
| RPE-04 | Turning on `valuation.view` also turns on `stock.read` and announces it; turning `stock.read` off turns the dependants off and announces it |
| RPE-05 | Changes lists exactly the differences from the saved role, with per-row Undo, and equals the review dialog |
| RPE-06 | The Access preview updates from unsaved ticks and shows "See unit costs: Yes, See stock value: No" correctly; menu, Home and phone tab lines match the app's own filters |
| RPE-07 | People tab lists the right people, links work, empty state shown; without `users.read` it shows only the count |
| RPE-08 | Review and save always opens the dialog; reason is required only for High impact grants and "Every warehouse" |
| RPE-09 | Save sends `If-Match`; a stale role shows the stale banner and keeps the ticks |
| RPE-10 | Leaving with unsaved ticks shows the guard; Discard restores the saved state; a session end keeps the ticks (R26) |
| RPE-11 | Admin editor is read-only with the reason; standard role names are read-only text; Reset to standard restores the standard set |
| RPE-12 | Applying a template changes ticks only; nothing is saved until Review and save |
| RPE-13 | An unknown permission key renders with the fallback text and does not break the page |
| RPE-14 | The catalog contract test fails if any catalog key lacks en, zh or ms text |

**Person's page and exceptions (RPU)**

| ID | Criterion |
|---|---|
| RPU-01 | Role card and Warehouses card are separate, save separately and have different headings; an "Every warehouse" role hides the warehouse list with a sentence |
| RPU-02 | Changing the role shows the live difference line before saving, with counts and a details disclosure |
| RPU-03 | Add exception requires a reason (5-200 characters); expiry is optional; a past date is refused; redundant options are disabled with the reason |
| RPU-04 | An exception shows in the header chip, the banner, the Exceptions table, the Users list and the effective list, always with text |
| RPU-05 | An expired exception stops applying (server) and moves to "Expired exceptions"; the effective list no longer lists it |
| RPU-06 | Effective list: every row shows Allowed / Not allowed and its source (role or exception with who, when, reason, expiry); filters All / Allowed / From exceptions work |
| RPU-07 | Compare person vs role lists exactly the exceptions; person vs person lists differences with the Differences-only toggle; the table is a keyboard grid |
| RPU-08 | Without `users.overrides` there is no Add exception button and the table is read-only; on one's own page it is disabled with the reason |

**Safety (RPS)**

| ID | Criterion |
|---|---|
| RPS-01 | Your own page is read-only with SF-01 text; Turn off account is absent |
| RPS-02 | Removing your own `roles.manage` through a role you hold is blocked in the review dialog with SF-02 text |
| RPS-03 | A permission you do not hold cannot be turned on (role or exception) and says why; a role with rights you do not hold is disabled in the role select |
| RPS-04 | Turning off, changing the role of, or removing rights from the last administrator is blocked with the SF-05..SF-09 text, before commit (dry-run) and again if the server answers 409 |
| RPS-05 | Admin role cannot be edited, retired or deleted through the UI or the API |
| RPS-06 | A signed-in person sees the "Your access was changed" banner within 30 seconds (or on the next request), the banner does not take focus, Refresh my access updates menu and Home without losing a form |
| RPS-07 | An action that is no longer allowed shows one inline message with the time and keeps the form; no duplicate toast |
| RPS-08 | A turned-off account gets the re-sign-in message and cannot continue |
| RPS-09 | Every write appears in the audit log with a sentence from 5.8 and the reason; the History and Activity links open the right filter |

**Accessibility, phone, density, theme (RPA)**

| ID | Criterion |
|---|---|
| RPA-01 | axe has 0 serious or critical issues on Roles, Role editor (each tab), Person's page (each tab), the four dialogs, in both themes |
| RPA-02 | The whole role editor can be completed by keyboard alone; no keyboard trap; visible focus (3:1) on every control; skip link works |
| RPA-03 | State is never colour-only (grayscale screenshot test of the editor and the effective list) |
| RPA-04 | At 320 px and 400 % zoom there is no horizontal page scroll, no clipped label, no sticky bar over a focused control |
| RPA-05 | On a 360 px phone every control is 44 px or more, the editor can be completed, the compare view shows at most two subjects |
| RPA-06 | Compact density at 1280 px shows at least 30 % more rows in the Roles list and exceptions table; checklist text stays 16 px |
| RPA-07 | Forced-colours: chips, rows and focus remain distinguishable |
| RPA-08 | Reduced motion: no animation is needed to understand any state |

### 8.2 Playwright scenarios

Real-API scenarios take the numbers 40..45 (reserved here; plan 2.3 holds 14..28). Stub-API cases live in `e2e/tests/stub/roles-*.spec.ts` and run on every pull request (plan N6). Page objects: `RolesPage`, `RoleEditorPage`, `UserAccessPage`, added by the slice that builds the screen.

| File | Scenarios |
|---|---|
| `40-roles-list.spec.ts` | (1) Admin sees five Standard roles, correct counts, Admin has no menu; (2) Add role from the Clerk template, name duplicate error, create succeeds, appears in list; (3) Retire a custom role with a person: still listed as Retired - 1 person; Delete disabled with reason; (4) Owner and Supervisor cannot open `/roles` (Forbidden); a custom role with only `roles.read` sees view-only |
| `41-role-editor.spec.ts` | (1) Create "Night-shift clerk" from Clerk, tick See stock value, preview says Yes, Changes shows 1, Review and save (reason optional because Sensitive is not High impact), saved toast; (2) bulk-select Administration skips High impact and says so; (3) dependency announcement; (4) Reverse posted documents requires reason, dialog shows High impact warning; (5) discard restores; guard on navigation; (6) stale role from a second context shows the stale banner; (7) Reset a changed Supervisor to standard; (8) keyboard-only completion; (9) axe in both themes |
| `42-user-access.spec.ts` | (1) Assign the new role to a staff user, warehouse card separate, difference line shown; (2) the staff user signs in (second context) and sees no cost; after the role gains `cost.view`, banner appears within 30 s, Refresh shows cost; (3) "Every warehouse" role hides the warehouse list; (4) effective list sources; (5) compare person vs role equals exceptions |
| `43-exceptions.spec.ts` | (1) Give `valuation.view` with reason and expiry; chip, banner, table, Users list show it; the person sees the stock value after refresh; (2) take away `document.post` by exception, Post disappears for the person; (3) expiry in the past refused; redundant option disabled; (4) remove exception with confirm; (5) High impact exception shows the second confirm; (6) expired exception moves to Expired (server clock helper) |
| `44-rights-change.spec.ts` | (1) Demote a signed-in Supervisor: banner within 30 s, Approvals gone after Refresh, direct URL shows Forbidden with the new sentence; (2) a dirty receipt editor keeps its lines when the person loses `document.post`, and Post fails with the inline sentence, one alert; (3) turn off an account: next call shows the re-sign-in message; (4) own page read-only; removing own `roles.manage` blocked; (5) last administrator cannot be turned off, demoted or stripped (dry-run text and 409 path); (6) escalation: a custom administrator without `valuation.view` cannot tick it nor assign a role that has it; (7) audit rows exist with reasons and the History link filters correctly |
| `45-roles-phone-a11y.spec.ts` | (1) 360 px: edit a role end to end, bottom bar, tabs, sheets; (2) 320 px and 400 % zoom reflow; (3) zh and ms smoke of the editor labels; (4) axe sweep of all new screens and dialogs in both themes; (5) grayscale screenshot comparison and forced-colours screenshot |

Stub cases (fast, per pull request): catalog failure, unknown permission key, 409 name, 409 stale, 409 last administrator, 403 permissions_changed, dry-run results, offline save blocked.

## 9. Frontend slice plan and owner questions

### 9.1 Slices (each under about 1,200 changed lines including specs)

Lane: a new lane **X Access** owns `web/libs/containers/administration/src/lib/roles/**`, `.../users/**` (the person's page), `web/libs/features/administration/**` (stores, catalog, rules). It shares the shell lane for the menu item (RP01, a few lines) and the table lane only by consuming `app-table`. "BE" ids are backend slices the screens need (names are mine; be-architect renames them from the ADR): BE-A catalog and roles read; BE-B roles write with dry-run and audit; BE-C person access (role, exceptions, effective); BE-D permission version and error codes; BE-E audit filters.

| Slice | Name | Size | Depends on (frontend) | Depends on (backend) | Priority |
|---|---|---|---|---|---|
| RP01 | Foundations: permission catalog store, constants, i18n keys for the catalog, pure rules (dependencies, bulk rule, diff, describeAccess), menu item and group relabel, permission-derived phone tabs and warehouse-scope flag (replaces the role-name maps), route guards | 800 | R06 (store), R10 (shell), R04 | BE-A | Must |
| RP02 | Roles list: table, filters, row menu, Add role dialog (blank, template, existing), retire, delete, empty/error states, views of S53/S55 | 900 | R08 (form), R13 (dialog), A02 (table), A04 (row action names), A01 (menu popover) | BE-A, BE-B | Must |
| RP03 | Role editor core: details card, checklist component (fieldsets, tri-state, flags, dependencies, bulk, locks), dirty state, unsaved guard, save without the dialog stub, stale banner, special roles (Admin, standard, retired, view only) | 1,100 | RP01, RP02, R11 (one failure), R14 (live regions, error summary), R26 | BE-A, BE-B | Must |
| RP04 | Role editor panels: Access preview, People, Changes (diff with Undo), templates apply and Reset to standard, Review and save dialog with dry-run, reason rules | 1,100 | RP03, A16 (drawer for "see what this role allows" is RP05) | BE-B (dry-run) | Must |
| RP05 | Person's page: tabs, Role and Warehouses cards with live difference line, Profile tab (moved from the dialog), create dialog update, Users list columns and filters, Effective permissions list with sources | 1,150 | RP01, RP03 (shared checklist/list pieces), A16 | BE-C | Must |
| RP06 | Exceptions: table, Add exception dialog (give/take, reason, expiry, dependency and redundancy rules, High impact second step), banners and chips, Expired section | 850 | RP05, R08 | BE-C | Should |
| RP07 | Safety and live rights change: SF-01..SF-13 messages and dry-run mapping, own-page read-only, permission-version header and 30 s check, "Your access changed" banner and [Refresh my access], 403 `permissions_changed` handling, Forbidden sentence, account-disabled path | 850 | RP05, R11, R26, R10 | BE-D, BE-B | Must (before release) |
| RP08 | Compare: `app-access-compare` (ARIA grid), role compare page, person vs role and person vs person, differences-only | 900 | A02, A04, RP05 | BE-A, BE-C | Could |
| RP09 | History links and Activity tab, audit sentences, palette entries, zh and ms sweep, docs update | 650 | RP05, A07 or the existing audit screen | BE-E | Should |
| RP10 | e2e: scenarios 40..45 and stub cases, page objects, axe and phone matrix entries | 900 | RP02..RP07 | - | Must (written with each slice, finished here) |

Total about 9,200 lines. Order: RP01, RP02, RP03, RP04, RP05, RP07, RP06, RP09, RP08, with RP10 written slice by slice. First usable result: after RP05 an administrator can add a role, set permissions, assign it to a person and see the effective list; RP07 must ship together with or before the first release because it holds the lockout and live-change behaviour. Parallelism: RP02 and RP01 are disjoint; RP05 and RP04 touch different folders (users vs roles) and can run together once RP03 has landed.

Reuse notes for fe-architect: the checklist and the effective list share one `app-permission-groups` component (edit mode and read mode) to keep both screens identical; the sentence function is a pure helper in `features/administration` and is tested with a table of every key; no role-name literal may remain in `web/libs` after RP01 (`PHONE_TABS_BY_ROLE`, `ALL_WAREHOUSE_ROLES`, `ROLE_LABELS` only for standard role display names, `approval-visibility.ts`; see section 10).

### 9.2 Questions only the product owner can answer (with my default)

| # | Question | Default assumed |
|---|---|---|
| Q1 | "Master Data should be able to add role": do you mean the Administration area (roles are added where administrators work) or that the people with the Master Data role should be able to add roles? | The area: Roles is under Administration, gated by `roles.manage`, held by Admin only. You can give `roles.manage` to any role later. |
| Q2 | Besides Admin, should anyone hold `roles.manage` or `users.overrides` from the start (for example the Owner)? | No. Only Admin. Owner keeps `audit.read` only. |
| Q3 | May the permissions of Owner, Supervisor, Warehouse staff and Clerk be changed, or must they stay as shipped (and you copy them to customise)? | They can be changed (with "Reset to standard"); Admin cannot. |
| Q4 | One role per person, or several roles on one person? | One role per person plus exceptions. |
| Q5 | Exceptions: is "take away" (deny) wanted as well as "give"? Must an exception always have an end date? | Both give and take away; reason always required; end date optional (7 days, 30 days, a date, or none). |
| Q6 | When you change rights, may a signed-in person keep the old rights for the 15 minutes the sign-in token lasts (known as SEC-06/ER-15), or must the old rights stop at once? | Stop at once; the person sees a "Your access was changed" message and refreshes in one click. This needs a server check on every request. |
| Q7 | A role that people still have: may it be deleted if the system moves them to another role you choose, or only retired and emptied by hand? | Retire only; delete is allowed when nobody has the role. People are moved one by one. |
| Q8 | Should giving a role "every warehouse" be a role setting (as designed) or only for the Admin and Owner roles as today? | A role setting, with a reason required when it is turned on. |

## 10. What the screens need from other roles

For be-architect (reconcile with `adr-roles-permissions.md` when it lands; where the ADR differs, the ADR wins and I adapt the copy):

1. **Approval level.** `ApprovalPolicy` ranks roles by name and approval rules store `roleRequired` (Supervisor, Owner, Admin); `approval-visibility.ts` in the web app also switches on role names. A custom role cannot be ranked. Needed: either an "approval level" attribute on a role (my 3.6 control) or approval rules that point at a role or a permission. Conflict to decide; the Settings rule form must use the same picker.
2. **Permission-driven chrome.** `PHONE_TABS_BY_ROLE`, `ALL_WAREHOUSE_ROLES` (`seesAllWarehouses`) and `ROLE_LABELS` are role-name based in the web app. Needed from the API: the person's `permissions[]`, `seesAllWarehouses` (from the role's warehouse access) and the role's display name; custom role names are user text and are not translated (standard roles keep translated names via a stable `systemKey`). The web will derive tabs from permissions (RP01).
3. **Catalog endpoint**: `GET /api/permissions` returning `key`, `group`, `sort`, `impact` (`normal`, `sensitive`, `high`), `requires[]`. Words stay in the web catalogs.
4. **Dry-run** for every write that changes access (role permissions, retire, delete, person's role, deactivation, exception): returns `affectedPeople[]` (count and first names), `selfAffected`, `blockedBy[]` with codes (`last_administrator`, `own_access`, `escalation`, `admin_role_locked`, `role_in_use`) and the permissions that would be lost. The real call enforces the same and answers `409` with the code.
5. **Roles API**: list with counts (people, permissions), created/modified at and by, `isSystem`, `isActive`, `changedFromStandard`, `warehouseAccess` (`assigned`/`all`), row version for `If-Match`; templates list (standard sets); copy-from on create.
6. **Person's access API**: role, warehouses, exceptions (permission, effect, reason, createdBy, createdAt, expiresAt), effective list with `source` per permission (role id or exception id), expired exceptions kept for display.
7. **Permission version** on every response (`X-Permissions-Version`), a cheap `GET /api/me/permissions-version`, error codes `permissions_changed` and `account_disabled`, and old rights refused at once (ER-15). If the security-engineer keeps a window, the banner copy stays; only RPS-06 timing changes.
8. **Audit**: entity types Role and User with ids, the `reason` field, filterable; entries scrubbed per the cost rule.
9. **Ids** are opaque strings (GUID ADR pending); no number parsing in new code.
10. **Limits**: role name 2-40, description 200, exception reason 5-200, role count cap (suggest 50), exceptions per person cap (suggest 20), enforced by the API with the same messages.

For fe-designer: look of the permission row, group header with tri-state box, flag chips, change marker, source tag, the side panel and tab strip, the Yes/No rows, the sticky bar on phone, the compare grid; icons `shield`, `eye`, `lock`, `plus`, `minus`; contrast pairs for the new chips.

For fe-architect: slices RP01..RP10, lane X, the shared `app-permission-groups`, the permission-version service in `core/identity`, e2e numbers 40..45, a catalog contract test.

For security-engineer: the 30-second poll and header (no cost data), no permission data in browser storage, ER-15 decision (Q6), escalation rules SF-10..SF-13 as server rules, audit of every write.

For qa personas: the IDs in section 8.1 are the test list; qa-edge: ER-01 behaviour with a dirty role, lockout paths, escalation; qa-novice: phone editing and wording; qa-power: compare and Users list.

## 11. What this file supersedes in earlier documents

| Where | Was | Now |
|---|---|---|
| `mvp-ux.md` 1.2 menu, last rows | "Users and roles" (Admin), Settings, Audit log in the Admin group | Administration group: Users, Roles, Settings, Audit log; each gated by permission (1.2 here) |
| `mvp-ux.md` S50 "Users and roles" | one screen with a role select and a warehouse list in a dialog | Users list + the person's page (S57); create stays a dialog; role names come from the API |
| `mvp-ux.md` 1.1 matrix | the fixed permission matrix | the seeded default of the five Standard roles; the live truth is the Roles screens |
| `2026-refresh-ux.md` 2.5, 6.9 | palette and menu per role listed by name | unchanged in behaviour; they already filter by permission; Roles is one more entry |
| Web code | `PHONE_TABS_BY_ROLE`, `ALL_WAREHOUSE_ROLES`, `ALL_ROLES` role lists | replaced by permission-derived values and the roles API (RP01) |
| `retest-edge-accessibility.md` ER-04, ER-10 | form focus and row-action names | applied to every new form from the start (6.2 items 9 and 4) |
