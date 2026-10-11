# IMS 2026 design refresh - sign-in page

Author: ui-ux-designer. Date: 2026-10-11. Status: decided. Ready for fe-designer (token use, logo SVG, screenshots), fe-architect (slice, contracts), fe-framework (build), behavior-test-engineer (`e2e/tests/25-login.spec.ts`), qa-* (re-checks).

The product owner asked that this be decided, not offered as options. Everything below is a decision with its reason. Nothing here is "TBD"; the only questions left are business questions and are in section 10.

Builds on `docs/design/2026-refresh-ux.md` (fixed rules FR-01..FR-09, required marker 3.3, dark mode 4, motion 7) and `docs/design/2026-refresh-visual.md` (tokens, contrast table 2.3, button and field states 3.1 and 3.2, banner 3.7). Where this file and `mvp-ux.md` 4.1 (S01 Login) differ, this file wins for sign-in. No code is written here. This file names tokens, never colour values.

## 0. Decisions in brief

| # | Decision | Main reason |
|---|---|---|
| 1 | Desktop (>= 1024px): split page, brand panel left (5 parts) and form right (7 parts), full height, no floating card. Tablet and phone (< 1024px): one column on the plain `surface`, no card, no grey canvas. | Removes the "small card on a grey field" look on every width; the form stays one column on the widths where a second column would squeeze it. |
| 2 | Logo = a symbol (isometric box on a rounded tile) plus the product name as live text. The name comes from `app.name`, so a rebrand is one catalog value. Drawn spec in section 2. | The product name must stay one token (reusable base project); live text scales with zoom and translates. |
| 3 | `h1` is "Sign in" (not "Welcome back"). Tagline "Know what is on every shelf". Plain sentence-case copy, no "please", no exclamation marks. | The heading names the task and matches the tab title (QA ER-16); "Welcome back" is wrong for a first-day user. |
| 4 | Brand panel uses the existing `accent-soft` tint with `on-accent-soft` text. No gradient, no image, no new token. | Calm, proven contrast in both themes (visual 2.3), zero palette work. |
| 5 | Language and appearance move from under the button to two menu buttons at the top right. | Reachable before reading, never pushed off screen by the keyboard, out of the way of the primary path. |
| 6 | No stored username, no "Remember me", no stored password. The browser password manager does that job. One exception: after a session expiry the username is kept in memory so the user only types the password. | Shared warehouse devices; the 7-day session already spares people from daily sign-in. |
| 7 | One banner above the button, `role="alert"`, cleared on any edit, on resubmit and on reload. It never appears together with empty-field messages. The same call raises no toast. | QA EA-26, ER-11; one failure, one announcement. |
| 8 | Lockout and rate-limit messages are built on the client from the numbers the API sends, so they are translated. They say what to do and when. | Today a Chinese or Malay user sees the English server sentence. |
| 9 | Session expiry returns the user to the page they were on; a dirty editor is never thrown away (separate slice, section 4.11). | QA ER-01 (Major). |
| 10 | Footer shows "Version x.y.z" only. No privacy link: no privacy page exists. | Support needs the version; a dead link is worse than none. |

### 0.1 What is wrong today (evidence)

| Finding | Source | Answered in |
|---|---|---|
| Small card on a large grey canvas, page feels unfinished | Product owner | 1 |
| Login `h1` says "IMS" while the tab title says "Sign in" | `retest-edge-accessibility.md` ER-16 | 3 |
| Lockout and rate-limit sentence shown twice (banner and toast), both `role="alert"` | EA-24, EA-26, ER-11 | 4.4 |
| Lockout shown only for someone who typed the right password (by design, SEC-08 trade-off); a user who forgot the password sees the generic sentence and keeps guessing | EA-13, SEC-08 | 3 (hint after 3 failures), 4.4 |
| Server sentences are English only | `login.component.ts` uses `error.detail` | 3, 4.4 |
| Language dropdown sits under the button, below the fold when the keyboard is open | Observed in code | 1.3, 4.7 |
| Session ends while the editor is dirty: "Discard changes?" with no reason, lines lost | ER-01 | 4.11 |
| Show/hide eye uses both `aria-pressed` and a changing label (double state) | `input.component.html` | 4.5 |
| Sign-in rate limit is shared with token refresh, 20 per minute per address, so an office behind one address can block itself | `e2e/src/rate-limit.ts`, EA-24 | 3 (copy names "this network") |
| Sign-in is the first screen every shift; staff on a phone with gloves | PRD users | 4.1, 5 |

## 1. Layout (decision 1)

### 1.1 The decision

| Width | Layout | Brand content shown |
|---|---|---|
| >= 1024px (desktop, tablet landscape) | Two columns, full viewport height, full bleed. Left brand panel (`accent-soft`), right form panel (`surface`). Columns `5fr 7fr`, brand panel at least 22rem. Form content centred in its column, 28rem wide at most. | Lockup, tagline, three points, version |
| 600 to 1023px (tablet portrait) | One column on `surface`. A header row: lockup at the start, language and appearance buttons at the end. Tagline under the lockup in muted text. Form block 28rem wide, centred, starts at about 12% of the height (not dead centre, so the keyboard does not push it off). | Lockup, tagline, version |
| < 600px (phone) | One column on `surface`. Compact header row (56px): lockup (tile 32px) at the start, buttons at the end. `h1`, form, access line. Page padding 16px. | Lockup, version. No tagline, no points |
| Height <= 500px at any width (landscape phone, keyboard open, 400% zoom) | Same as the width rule, but the subtitle and the tagline are hidden and the lockup shrinks to its phone size. | Lockup only |

### 1.2 Why a split panel on desktop and not a centred card

1. The owner's complaint is the floating card on grey. A card on any background still leaves a large empty field around it. A split page fills the screen with two purposeful areas.
2. The brand panel carries the one thing a centred card cannot: what the product is, in the first second, for a visitor who has never seen it (a new supervisor, an auditor, a customer demo).
3. It costs nothing in task speed: the form column is the same width as the old card and autofocus puts the cursor in it. The brand panel has no control the user must pass.
4. A split layout only works when the form column keeps its width. Below 1024px a second column would shrink the form below 28rem at 768px (tablet portrait) and is useless on a phone, so there the brand collapses into the header. That is why the breakpoint is the design system's 1024px and not lower.

### 1.3 Why language and appearance move to the top right

| Question | Answer |
|---|---|
| Why not under the button? | With the on-screen keyboard open on a phone, anything under the button is off screen. A user who needs another language must find it before reading. |
| Why not in the form? | Language is not part of signing in, so it must not sit in the tab path between the password and the button. |
| Why buttons with a menu, not a select? | A select looks like a form field and invites "is this required?". A labelled button with a globe icon is recognised in any language. On a phone the menu opens as a bottom sheet with three 48px rows (easy with gloves); on desktop as a small menu. |

### 1.4 Wireframes

Desktop 1280 (brand panel left, form right):

```
+--------------------------------------------+----------------------------------------------------------+
|  [#] IMS                                   |                          [globe English v]  [sun/moon v] |
|                                            |                                                          |
|                                            |                                                          |
|  Know what is on                           |                      Sign in                             |
|  every shelf                               |                      Enter your email or username        |
|                                            |                      and password.                       |
|  (v) Every warehouse in one live view      |                                                          |
|  (v) Approvals with a full audit trail     |                      * means required                    |
|  (v) Made for barcode scanners and phones  |                                                          |
|                                            |                      Email or username *                 |
|                                            |                      [ (u)                              ]|
|                                            |                      Password *                          |
|                                            |                      [ (k)                      (eye)  ]|
|                                            |                                                          |
|                                            |                      +--(!) Email, username or password |
|                                            |                      |     is wrong.                     |
|                                            |                      +-----------------------------------+
|                                            |                      [            Sign in              ]|
|                                            |                                                          |
|                                            |                      Need access or forgot your          |
|                                            |                      password? Ask your administrator.   |
|  Version 0.1.0                             |                                                          |
+--------------------------------------------+----------------------------------------------------------+
```

`(u)` and `(k)` are leading icons (`user`, and a key or lock glyph from the existing set; if no key exists, use no icon on the password field). Leading icons are decorative (`aria-hidden`) and are kept only if the visual designer can draw them with the existing stroke set; they must never replace the label.

Tablet 768 (portrait):

```
+--------------------------------------------------------------+
|  [#] IMS                    [globe English v]  [sun/moon v]  |
|  Know what is on every shelf                                 |
|                                                              |
|                                                              |
|              Sign in                                         |
|              Enter your email or username and password.      |
|              * means required                                |
|              Email or username *                             |
|              [                                    ]          |
|              Password *                                      |
|              [                              (eye) ]          |
|              [              Sign in              ]          |
|              Need access or forgot your password?            |
|              Ask your administrator.                         |
|                                                              |
|  Version 0.1.0                                               |
+--------------------------------------------------------------+
```

Phone 360:

```
+------------------------------------+
| [#] IMS              [EN v] [sun v]|   <- 56px row, buttons 44px
|                                    |
| Sign in                            |
| * means required                   |
|                                    |
| Email or username *                |
| +--------------------------------+ |
| |                                | |   <- 48px
| +--------------------------------+ |
| Password *                         |
| +----------------------------(eye)+|
| |                                | |
| +--------------------------------+ |
| +--------------------------------+ |
| | (!) Email, username or         | |   <- banner, role=alert
| |     password is wrong.         | |
| +--------------------------------+ |
| +--------------------------------+ |
| |            Sign in             | |   <- 56px
| +--------------------------------+ |
| Need access or forgot your         |
| password? Ask your administrator.  |
|                                    |
| Version 0.1.0                      |
+------------------------------------+
```

Phone with the keyboard open (height <= 500px): header row, `h1`, the two fields and the button stay in one scroll; the access line and version scroll below. The focused field is scrolled clear of the keyboard (`scroll-margin-block`).

Locked account (phone):

```
| +--------------------------------+ |
| | (!) Too many failed attempts.  | |
| |     Try again in 15 minutes.   | |
| |     Or ask your administrator  | |
| |     to unlock the account.     | |
| +--------------------------------+ |
```

Session expired (desktop, form panel only):

```
|                      Sign in                             |
|                      +--(i) Your session expired.       |
|                      |      Sign in again.               |
|                      +-----------------------------------+
|                      Email or username *                 |
|                      [ prefilled from this session      ]|
|                      Password *                          |
|                      [ cursor here                  (eye)]|
```

### 1.5 Visual rules (for fe-designer)

1. Page background on this route is `surface` (not `canvas`). The form panel has no card, border or shadow. Inputs keep the `border-strong` boundary from the field spec, which is what separates them from the page.
2. Brand panel: background `accent-soft`; tagline `on-accent-soft`; the three points `text`; the check icons `accent` (6.31 and 6.63, above 3); version `text-muted` (6.48 and 6.53, above 4.5). All of these pairs are already in the contrast table; no new pair is needed, so `npm run contrast` stays 162 checks. A 1px `border` separates the panel from the form panel (visible in forced colours).
3. In dark the same tokens give a deep navy panel with pale text, which is calm and not a bright slab. Dark is checked by screenshot (section 7, LG-30).
4. No gradient, no image, no pattern, no blur, no animation on load. The page is static.
5. Type: `h1` step 5 (24px, 600). Tagline step 5 (24px, 600, at most 16em wide). Points step 3 (16px). Subtitle step 3, `text-muted`. Legend step 2. Access line step 2, `text-muted`; version step 1.
6. Spacing: the form column uses `space(2)` between fields and `space(3)` between the heading block and the form. Brand panel padding `space(6)` (48px) on desktop.
7. Phone controls: fields 48px (`height('touch')`), the Sign in button 56px (`height('bar')`), icon buttons 44px minimum (`height('touch-min')`).

## 2. Logo and wordmark (decision 2)

### 2.1 Concept

A box seen from the corner (an isometric cube, the unit of stock) on a rounded square tile. It is the shape the app icon already uses (`favicon.svg`, `icons/icon.svg`), so the PWA icon, the favicon and the page agree. The wordmark is the product name set as live text next to it. There is no tagline in the logo.

### 2.2 Symbol, drawn on a 32 x 32 grid (for the SVG)

| Part | Spec |
|---|---|
| Canvas | `viewBox="0 0 32 32"`, no fixed width or height in the file (sized by CSS) |
| Tile | Rounded rectangle, x 0, y 0, width 32, height 32, corner radius 6 |
| Box outline | One closed hexagon, round joins, no fill. Points in order: (16, 3) top, (27.26, 9.5), (27.26, 22.5), (16, 29) bottom, (4.74, 22.5), (4.74, 9.5) |
| Box edges | Three lines meeting at the centre (16, 16): to (4.74, 9.5), to (27.26, 9.5), to (16, 29) |
| Stroke | 2.5 units, round line cap and join, `fill="none"` on the glyph |
| Safe area | Glyph stays inside x 4.74..27.26 and y 3..29 (a margin of 3 units to the tile edge) |
| Paint | No literal colours in the file. The tile uses a CSS variable (`accent`), the glyph strokes `currentColor` set to `on-accent`. Forced-colours mode and a mono use then work without a second file |
| Accessibility | The symbol is decorative: `aria-hidden="true"`, no `<title>`. The name next to it is real text |

Two exports: `logo-tile.svg` (tile plus glyph) and `logo-glyph.svg` (glyph only, `currentColor`, for monochrome and forced colours). The favicon and `icon.svg` get the same geometry; fe-designer recolours them to the new accent and adds the dark variant inside the SVG with `prefers-color-scheme`, as visual 5.2 already requires.

### 2.3 Lockup (built in HTML and CSS, not baked into an image)

| Property | Desktop (>= 1024px) | Tablet | Phone |
|---|---|---|---|
| Tile size | 40px | 40px | 32px |
| Gap tile to name | 12px | 12px | 8px |
| Name | Live text from `app.name`, weight 600, step 6 (32px) | step 5 (24px) | step 5 (24px) |
| Alignment | Name vertically centred on the tile | same | same |
| Clear space | At least half the tile size on all sides | same | same |
| Minimum | Tile 24px (never smaller) | | |

The name uses the normal text colour of its surface (`on-accent-soft` on the brand panel, `text` elsewhere), letter spacing 0. It is not an image, so it scales with zoom, follows the language, and a rebrand changes `app.name` only.

### 2.4 Don'ts

No gradient on the tile, no shadow, no outline effect, no animation, no tagline inside the logo, no stretching, no placing the tile on a busy background.

## 3. Copy (decision 3)

Rules: sentence case, no "please", no exclamation marks, plain words, say what to do. Product name is always the token `{appName}`, never typed into a sentence. zh and ms are drafts and carry `TODO-review` in the catalogs (K15). "existing" means the key is already in the catalogs and keeps its text.

| Key | en | zh (draft) | ms (draft) |
|---|---|---|---|
| `login.title` (the `h1` and the tab title "Sign in - {appName}") | Sign in | 登录 | Log masuk |
| `login.subtitle` | Enter your email or username and password. | 请输入邮箱或用户名和密码。 | Masukkan e-mel atau nama pengguna dan kata laluan anda. |
| `login.tagline` | Know what is on every shelf | 掌握每个货架上的库存 | Tahu apa yang ada di setiap rak |
| `login.point.warehouses` | Every warehouse in one live view | 所有仓库，一个实时视图 | Semua gudang dalam satu paparan langsung |
| `login.point.audit` | Approvals with a full audit trail | 审批流程，全程留痕 | Kelulusan dengan jejak audit penuh |
| `login.point.devices` | Made for barcode scanners and phones | 支持扫码枪和手机 | Dibuat untuk pengimbas kod bar dan telefon |
| `login.username` (existing) | Email or username | existing | existing |
| `login.password` (existing) | Password | existing | existing |
| `form.requiredLegend` (UX 3.3) | * means required | UX 3.3 | UX 3.3 |
| `login.username.empty` | Enter your email or username. | 请输入邮箱或用户名。 | Masukkan e-mel atau nama pengguna anda. |
| `login.password.empty` | Enter your password. | 请输入密码。 | Masukkan kata laluan anda. |
| `ui.showPassword` (existing) | Show password | existing | existing |
| `login.capsLock` | Caps Lock is on. | 大写锁定已开启。 | Caps Lock dihidupkan. |
| `login.submit` (existing) | Sign in | existing | existing |
| `login.busy.status` (screen-reader only) | Signing in | 正在登录 | Sedang log masuk |
| `login.busy.slow` (visible after 8 s) | Still signing in. This is taking longer than usual. | 仍在登录，耗时比平时长。 | Masih log masuk. Ini mengambil masa lebih lama daripada biasa. |
| `login.error.invalid` (existing) | Email, username or password is wrong. | existing | existing |
| `login.error.invalid.repeat` (second line from the 3rd wrong attempt in a row) | Repeated wrong attempts lock the account for a while. Ask your administrator if you are not sure of your details. | 多次输入错误会暂时锁定账户。如不确定登录信息，请联系管理员。 | Percubaan salah berulang akan mengunci akaun seketika. Tanya pentadbir anda jika tidak pasti tentang butiran anda. |
| `login.error.locked.one` | Too many failed attempts. Try again in 1 minute. | 失败次数过多。请 1 分钟后重试。 | Terlalu banyak percubaan gagal. Cuba lagi dalam 1 minit. |
| `login.error.locked.other` | Too many failed attempts. Try again in {minutes} minutes. | 失败次数过多。请 {minutes} 分钟后重试。 | Terlalu banyak percubaan gagal. Cuba lagi dalam {minutes} minit. |
| `login.error.locked.admin` (second line, ships with the unlock action, see 4.4) | Or ask your administrator to unlock the account. | 或联系管理员解锁账户。 | Atau minta pentadbir anda membuka kunci akaun. |
| `login.error.rateLimited` | Too many sign-in attempts from this network. Try again in {seconds} seconds. | 此网络的登录尝试次数过多。请 {seconds} 秒后重试。 | Terlalu banyak percubaan log masuk dari rangkaian ini. Cuba lagi dalam {seconds} saat. |
| `login.error.rateLimited.minutes` (when the wait is 90 seconds or more, rounded up) | Too many sign-in attempts from this network. Try again in {minutes} minutes. | 此网络的登录尝试次数过多。请 {minutes} 分钟后重试。 | Terlalu banyak percubaan log masuk dari rangkaian ini. Cuba lagi dalam {minutes} minit. |
| `login.error.rateLimited.moment` (wait of 1 second or less) | Too many sign-in attempts from this network. Try again in a moment. | 此网络的登录尝试次数过多。请稍后重试。 | Terlalu banyak percubaan log masuk dari rangkaian ini. Cuba sebentar lagi. |
| `error.network` (existing) | Can't reach the server. Check your connection and try again. | existing | existing |
| `login.offline` | You're offline. Connect to the internet to sign in. | 您当前处于离线状态。请连接网络后登录。 | Anda di luar talian. Sambung ke internet untuk log masuk. |
| `login.error.server` | Sign-in is not working right now. Try again in a moment. | 暂时无法登录。请稍后重试。 | Log masuk tidak berfungsi sekarang. Cuba sebentar lagi. |
| `login.sessionExpired` (existing) | Your session expired. Sign in again. | existing | existing |
| `login.access` | Need access or forgot your password? Ask your administrator. | 需要开通账户或忘记密码？请联系管理员。 | Perlukan akses atau terlupa kata laluan? Tanya pentadbir anda. |
| `login.version` | Version {version} | 版本 {version} | Versi {version} |
| `login.language.button` (accessible name) | Language: {language} | 语言：{language} | Bahasa: {language} |
| `login.appearance.button` (accessible name) | Appearance: {choice} | 外观：{choice} | Paparan: {choice} |
| Language menu items | English, 中文, Bahasa Melayu (each in its own language, never translated) | | |
| Appearance menu items (reuse UX 4.4) | Use device setting, Light, Dark | | |
| `login.devHint` (existing, development builds only) | existing | existing | existing |

Notes on the copy:

1. The access line extends the owner's "Need access? Ask your administrator." with "or forgot your password", because there is no self-service reset in the MVP (PRD, `mvp-ux.md` 4.1 note 4) and a forgotten password is the commonest reason to be stuck. It adds no link and no email address.
2. The wrong-credentials sentence stays exactly as the E7 copy: it never says which field was wrong or whether the account exists.
3. The second line of the wrong-credentials banner appears from the third consecutive failure on this page. It counts on the device only; it does not tell the user how many attempts remain (the client cannot know) and it does not confirm that the account exists.
4. The lockout text carries the wait ("in 15 minutes") and the other way out (the administrator). The wait is the number the API sends (`retryAfterMinutes`), never a number typed into the client.
5. The rate-limit sentence says "from this network" on purpose. The limiter is per address and shared with token refresh, so a whole office can trip it; "the user did something wrong" would be false.
6. Taglines and points must stay true to the release. "Barcode scanners" means keyboard-type scanners in the product picker (Phase 1). Do not promise stocktake or offline in this page.
7. No privacy or terms link: there is no privacy page in the app or the docs. If one is added later, it goes in the footer after the version (section 10).

## 4. Behaviour (decision 4)

### 4.1 States of the page

| State | Banner | Fields | Button | Focus |
|---|---|---|---|---|
| Idle (first load) | none | empty, no messages | "Sign in", enabled | Username (desktop and phone, 4.2) |
| Idle after session expiry | status (warning) "Your session expired. Sign in again." | username prefilled from memory, password empty | enabled | Password |
| Submit with an empty field | none (a banner from an earlier attempt is removed first) | message under each empty field, `aria-invalid` | enabled | First field with a message |
| Busy | none (old banner already removed) | `readonly` (not `disabled`) | `aria-disabled`, `aria-busy`, spinner, label kept | stays where it was |
| Wrong credentials | danger alert, `login.error.invalid` (+ repeat line from the 3rd failure) | kept; password selected on the next focus | enabled | stays where it was |
| Locked account | danger alert, `login.error.locked.*` (+ admin line when 4.4 ships) | kept | enabled | stays |
| Rate limited (429) | warning alert, `login.error.rateLimited*` | kept | enabled (the wait may already have passed) | stays |
| Offline (before submit) | status (warning) `login.offline`, shown as soon as the browser reports offline, removed when it reports online | editable | enabled (the browser flag is unreliable, never block) | unchanged |
| Server unreachable or 5xx | warning alert, `error.network` or `login.error.server` | kept | enabled | stays |
| Success | none | keep `readonly` | stays busy until the next page has rendered, so the form never flashes back to idle | the new page's `h1` (route announcer) |

Banner colour rule: danger is for "your input or account is the cause" (wrong credentials, locked). Warning is for "the environment is the cause, try later" (rate limit, offline, network, server). Both carry an icon and text; colour never carries the meaning alone (FR-08).

### 4.2 Focus, autofocus, Enter

1. On load the username field receives focus through `data-autofocus` (the repo rule; the HTML `autofocus` attribute is banned). It applies on every device. On a phone the keyboard may open and cover the lower part of the page, which is why the layout works with the keyboard open (section 5.2). Reason: this is the only thing the user came to do; skipping one tap per shift matters more than a shifted layout.
2. With a prefilled username (session expiry), focus goes to the password field.
3. Enter in either field submits the form (a real `<form>` with a real submit button; no key handler needed). While busy a second Enter or click is ignored; exactly one request is sent.
4. Tab order: skip link (existing) -> language button -> appearance button -> username -> password -> show-password button -> Sign in. The two buttons come first because the header comes first in the document; autofocus means most users never meet them on the way. The show-password button is in the tab order (keyboard users must be able to reach it).
5. Invalid submit: focus goes to the first field with a message (existing EA-06 behaviour).
6. After a failed sign-in focus is not moved. It stays where it was (the password field after Enter, the button after a click). Moving it would interrupt the alert announcement (one failure, one announcement). The password text is selected on the next focus, so typing replaces it.
7. `enterkeyhint` on the phone keyboard: "next" on the username, "go" on the password.

### 4.3 Validation and messages

1. The required messages ("Enter your email or username.", "Enter your password.") appear only after the first submit attempt, then update live as the user types. A user who taps into a field and out again (to reach the eye button) is not shouted at. This changes the current behaviour where `markAllAsTouched` is the only trigger and blur on an empty field can also show the message; the rule is "after the first submit".
2. Messages say what to do, are linked with `aria-describedby`, set `aria-invalid="true"`, and do not repeat the word "required" (UX 3.3 item 7).
3. The username is trimmed before sending. The password is never trimmed or altered. Maximum lengths come from `MAX_USERNAME_LENGTH` and `MAX_PASSWORD_LENGTH` (no counter shown).
4. Paste is never blocked in either field.
5. No password-strength rule or format hint on sign-in (that is for create and change password).

### 4.4 Errors: banner placement, clearing, wording, roles

1. Placement: between the password field and the Sign in button, full width of the form (the user's eyes and thumb are already there; matches `mvp-ux.md` E7). The session-expired status banner is above the first field, under the subtitle.
2. Role: error banners are `role="alert"` (result of an action). The session-expired banner is `role="status"`. The banner element is created when the failure arrives; the text is never changed in place.
3. Clearing, all of these remove the banner at once:
   - any edit in either field (value change);
   - starting a new submit (before validation, so an invalid resubmit shows only field messages);
   - a language change (the banner is then rebuilt in the new language from its stored data, see item 6, not removed);
   - a reload or any navigation (nothing about the error is stored anywhere).
   A stale error therefore can never sit next to empty-field messages (R02 / D3).
4. One announcement: the sign-in call is flagged so the global error interceptor shows no toast for it (R11 flag; ER-11). The banner is the only announcer. A test counts `role="alert"` and expects exactly 1.
5. Lockout (HTTP 401, code `account_locked`, `retryAfterMinutes`):
   - Line 1: `login.error.locked.one` or `.other` with the API's number.
   - Line 2: `login.error.locked.admin` ("Or ask your administrator to unlock the account.").
   - Tester note: the API shows the lockout only when the correct password is typed (it never confirms an account exists). A wrong password on a locked account, or on an unknown name, still returns the generic sentence.
   - **Dependency (backend):** an administrator cannot unlock an account today. Saving a user with a new password does not clear `LockoutUntil`, and no unlock action exists, so line 2 would be a promise the product cannot keep. Line 2 ships in the same release as an unlock action for the Users screen (backend item B7, section 8). Until then the banner has line 1 only. The 15-minute lock, the five-attempt limit and SEC-08 (anyone who knows a username can lock it) are existing trade-offs; B7 is also the practical remedy for a locked admin seen in QA.
6. The error is stored as data (kind and numbers), not as a finished sentence, and rendered through the catalog. A language change re-renders the banner in the new language. The API sentence (`detail`) is only a fallback for an unknown error with no mapped copy.
7. Rate limit (HTTP 429, `retryAfterSeconds`): the sentence names the wait. It is static text, with no live countdown (a ticking alert would chatter on a screen reader). The button stays enabled: the user may press again after the wait, and a second 429 returns a fresh number.
8. Offline and unreachable: before submit, if `navigator.onLine` is false the status banner shows `login.offline`. The button is not disabled. A submit that fails at network level shows `error.network` as a warning alert. Sign-in needs the server; there is no offline sign-in in the MVP (offline is Phase 2).
9. Any other 4xx/5xx: `login.error.server`. The reference id of the problem response, if present, is not shown on this page.
10. Wrong credentials counter: held in memory for the life of the page, reset by a successful sign-in or reload. It only decides whether the second line is shown.

### 4.5 Password field: show/hide, caps lock, password managers

1. Show/hide: a button inside the field at the end, 44 x 44px hit area inside the 48px field; the field's end padding leaves room so text never runs under it. It has a constant accessible name "Show password" and `aria-pressed` for the state. (Today it changes the label and also sets `aria-pressed`, which a screen reader reads as "Hide password, pressed"; use one or the other. Constant label plus `aria-pressed` is the rule.)
2. Pressing the button must not take focus away from the password field with a mouse or touch (prevent the pointer-down default), so the phone keyboard stays open and the caret stays put. With the keyboard, focus is on the button as normal.
3. The password goes back to hidden when the form is submitted, and when the page is hidden (tab switch or app switch). It does not go back on blur, because tapping the eye blurs the field.
4. Caps lock hint: while the password field has focus and a key event reports Caps Lock on, show `login.capsLock` under the field with the `alert` icon in muted text (not a banner, not `role="alert"`). It is linked with `aria-describedby`. It disappears on blur, when Caps Lock turns off, and while the password is revealed (the user can see the letters). Browsers that cannot report the state (some phones) simply never show it.
5. Password manager friendliness:
   - A real `<form>` with a submit button; the fields are not inside a dialog or re-created after submit.
   - Username: `name="username"`, `autocomplete="username"`, `type="text"`, `inputmode="email"`, `autocapitalize="none"`, `autocorrect="off"`, `spellcheck="false"`. (`type="email"` is wrong: usernames are allowed.)
   - Password: `name="password"`, `autocomplete="current-password"`, `autocapitalize="none"`, `autocorrect="off"`, `spellcheck="false"`.
   - Never `autocomplete="off"`, never block paste, never clear the fields on a successful submit before the navigation (the manager needs the values at submit time).
   - Stable `id`s and `name`s across releases and across languages (managers key on them).

### 4.6 Remembered username (decision, with the security view)

No. The app stores no username, no password and no token on the device (the refresh token is an HttpOnly cookie, SEC-34; `localStorage` holds only the language and the theme device copy).

| Reason | Detail |
|---|---|
| Shared devices | Warehouse tablets and phones are shared; a stored username shows who signed in last and invites account-lock mischief (SEC-08). |
| Low value | The session lasts 7 days (refresh cookie), so a person signs in about once a week, not every shift. |
| Better tool exists | The browser or phone password manager fills both fields under the user's own control and protection; section 4.5 makes sure it works. |
| No "Remember me" box | Session length is a fixed policy; a checkbox that implies a choice the product does not give would be misleading. |

The one exception: when the session expires while the app is open, the username of the signed-out user is kept in memory (one variable, never written to storage) and prefilled on the sign-in page, with focus on the password. It is dropped on explicit sign-out, on reload and when the next user signs in. Reason: on a phone typing an email is the slowest part of signing in, and in this case we already know who it is. security-engineer to confirm (section 9).

### 4.7 Language menu and appearance switch

| Aspect | Decision |
|---|---|
| Placement | Two buttons at the end of the header row (top right in left-to-right scripts). Language first, appearance second. Always visible, never inside the form. |
| Language button | Globe icon plus the current language written in its own script: "English", "中文", "Bahasa Melayu". From 400px width down it shows a short code ("EN", "中文", "BM") to fit 320px; the accessible name is always "Language: Bahasa Melayu". 44px high at least. |
| Language menu | Desktop and tablet: small menu under the button. Phone: bottom sheet. Three rows (48px on phone), the current one marked with a check icon and `aria-checked`, each row in its own language. Roles: `menu` with `menuitemradio` items. Choosing applies at once, closes the menu and returns focus to the button. No confirm. |
| Effect | Text on the page changes, `<html lang>` changes, the tab title changes. Anything typed in the fields is kept. The banner, if any, is rebuilt in the new language (4.4 item 6). |
| Persistence | `localStorage` key `ims.language` (existing). Before sign-in the choice lives on the device. It stays after sign-out. |
| Appearance button | Icon-only button showing the current choice (`monitor` for device setting, `sun`, `moon`), 44px square at least, with the accessible name "Appearance: Use device setting" (and a tooltip with the same words). It is the only icon-only control on the page; the menu it opens shows the full words. `monitor` comes from R05; `sun`, `moon`, `globe` already exist. |
| Appearance menu | Three rows: Use device setting (sub-line "Now: Dark" or "Now: Light"), Light, Dark; current one checked. Same menu/sheet pattern. Applies at once, no animation (UX 4.2 item 2). |
| Persistence | The choice made on this page is written to the device copy (`ims.theme`) and applies on the next load before first paint (`theme-init.js`). At sign-in: if the user made a choice on this page during this visit, it is applied and saved as that user's preference. If not, the user's own saved preference applies (default: device setting). At sign-out the device copy returns to the device setting (UX 4.2 item 3, plan C5). So Dark chosen on the sign-in page is never lost the moment the user signs in. |
| Cross-tab | A change updates other open sign-in tabs (storage event), same as UX DK-08. |
| Private mode | Storage blocked: the choice works until reload; no error shown. |

### 4.8 Busy state of the button

1. On submit the button gets `aria-disabled="true"` and `aria-busy="true"` immediately (not `disabled`, so focus stays and the browser does not drop it). Clicks and Enter are ignored. The label stays "Sign in"; a 20px spinner replaces the leading icon after 150ms (visual 3.1, UX 7.2). Colours do not change, so contrast does not change.
2. The two fields become `readonly` so the values cannot change mid-request and the password manager is not disturbed.
3. A polite, visually hidden status says "Signing in" once. With reduced motion the spinner becomes the static `clock` icon plus the visible word "Working" (R07).
4. After 8 seconds a visible line under the button says "Still signing in. This is taking longer than usual." (`role="status"`). The request is not cancelled by the page.
5. When the request fails the busy state ends and the banner appears. On success the busy state stays until navigation has finished.

### 4.9 Session expiry (return to the page the user was on)

1. Trigger: an API call is rejected (401), the silent refresh fails, and the user has no unsaved work (4.11 covers unsaved work). The app goes to `/login?returnUrl=<path>&reason=expired`.
2. The sign-in page shows the status banner `login.sessionExpired`, prefilled username (4.6) and focus on the password. The username field carries `aria-describedby` to the banner, so a screen-reader user hears the reason when focus lands.
3. After sign-in the app navigates to `returnUrl`. The return URL is accepted only if it starts with a single `/`, does not start with `//`, and is not `/login`; anything else goes to Home (`safeReturnUrl` today, plus the `/login` loop case).
4. If a different person signs in than the one whose session expired, the same return URL is used; if that person may not open it, the normal "You don't have access to this." page appears. Nothing from the previous user's session (prefilled username, drafts, Recents) carries over.
5. A signed-out visitor who opens a deep link goes to `/login?returnUrl=...` without the `reason` and sees no banner, then lands on the link after sign-in (existing e2e `08-session`).

### 4.10 Sign-out and shared devices

After explicit sign-out the sign-in page shows no username, no banner and the device language and device appearance. The previous user's per-user preferences are not applied (UX DK-02).

### 4.11 Session ends while a form has unsaved work (QA ER-01, Major)

This is a separate slice from the page redesign, but the sign-in page must support it, so the behaviour is decided here.

| Situation | Behaviour |
|---|---|
| Clean screen | 4.9 (redirect with `reason=expired`). |
| Dirty editor (unsaved lines) | The app does not navigate. The unsaved-changes guard ("Discard changes?") is not shown. A dialog opens over the editor: title "Your session expired", text "Sign in again to keep your work.", the same two fields (username prefilled, password focused), the same banner rules, a Sign in button, and a quiet "Leave without saving" button that asks for confirmation before it discards. |
| After a successful sign-in in the dialog | If it is the same user, the dialog closes and the failed request is repeated once; nothing typed is lost; focus returns to the control that had it. If it is a different user, the draft is discarded without being shown to them and the app goes to their Home. |
| Fallback until this slice exists | At minimum: when the refresh fails, skip the unsaved-changes guard, and show on the sign-in page "Your session expired. Sign in again." with the return URL, and say that unsaved changes could not be kept. |

No draft is written to `localStorage` or `sessionStorage` (cost data would remain on a shared device). The data stays in memory in the open page, which is why the dialog does not navigate away.

### 4.12 Things this page does not do

No "forgot password" flow, no "remember me", no social or single sign-on buttons, no CAPTCHA, no marketing carousel, no cookie banner (no tracking cookies exist), no animation on load, no welcome toast after sign-in.

## 5. Responsive, zoom, forced colours, reduced motion (decision 5)

### 5.1 Width rules from 320px to 1920px

| Width | Rule |
|---|---|
| 320 to 359 | The minimum supported. Page padding 16px (288px of content). Header row wraps: lockup first, the two buttons below or beside depending on room; the language button shows its short code. No horizontal scroll. Button label may wrap. |
| 360 to 599 | Phone layout of 1.1. Content 100% of the width up to 28rem. |
| 600 to 1023 | One column, form block 28rem, centred horizontally, starting at about 12% of the height. Tagline visible. |
| 1024 to 1439 | Split layout. Brand panel 5fr (at least 22rem), form panel 7fr. |
| 1440 to 1920 and wider | Same split. The brand content is capped at 28rem inside the panel and the form block at 28rem inside its column; extra width becomes empty space of the two panels (the panels still fill the screen). Nothing stretches. |

### 5.2 Height, keyboard and zoom

| Condition | Rule |
|---|---|
| Height <= 500px (landscape phone, keyboard open) | Subtitle, tagline and points are hidden (`display: none`, so also removed from the reading order). The page scrolls as one column. The focused control is scrolled into view with `scroll-margin-block`. Nothing is fixed to the bottom of the viewport (no sticky button). |
| 200% text zoom (text size only) | All sizes in `rem`; boxes use `min-height`, never `height`; labels, banner text and buttons wrap; nothing is clipped (WCAG 1.4.4, 1.4.12). |
| 200% page zoom | Effective width 640px on a 1280 screen: tablet layout (single column), correct. |
| 400% page zoom (WCAG 1.4.10 reflow) | Effective width 320px, height about 180px: phone layout with the short-height rules. The page scrolls vertically only; `scrollWidth` equals `clientWidth`; all controls reachable. |
| Safe areas | Page padding adds `env(safe-area-inset-*)` so a notch or a rounded corner never covers the header buttons. |

### 5.3 Forced colours (Windows High Contrast)

1. Every boundary is a real border (`border: 1px solid transparent` pattern, never `border: 0`) so the system draws it: fields, buttons, banner (all four sides), the two header buttons, the menus.
2. The brand panel's tint disappears in forced colours, so it gets a 1px border on its inline end that shows. The tile and the glyph use `currentColor`/system colours; the tile gets a visible border. The logo must still be recognisable as a box on a tile.
3. The banner's meaning stays in its icon and its text. The coloured edge is a bonus.
4. Focus rings use `outline` (kept by the browser). Do not set `forced-color-adjust: none` on anything on this page.
5. The asterisk and caps-lock hint keep their text.

### 5.4 Reduced motion

The page has no entrance animation and no animated background. The only motion is the busy spinner, which follows R07 (static `clock` icon plus "Working"). Menus and sheets open without translation under `prefers-reduced-motion: reduce`. The banner appears without a slide or fade.

### 5.5 Dark mode

Follows the device setting by default (UX 4.2). Tokens only; no image changes between themes (the symbol is two paints). The brand panel, banner, asterisk, caps-lock hint, focus ring and menus are checked in dark by axe and a screenshot review (LG-30).

## 6. Accessibility (decision 6)

### 6.1 Structure

| Element | Role / landmark | Content |
|---|---|---|
| `<header>` | `banner` | Lockup (symbol `aria-hidden`, name as text), language button, appearance button |
| `<main>` | `main` | `h1` "Sign in", subtitle, session-expired banner, legend, the form, banners, access line |
| `<aside aria-label="About {appName}">` | `complementary` | Tagline, the list of three points. Present only at >= 1024px (hidden below, not just visually) |
| `<footer>` | `contentinfo` | Version; the development hint in development builds only |

1. DOM order: header, main, aside, footer. On desktop the aside and footer are placed in the left column by grid placement. This is a deliberate exception to the "reading order equals visual order" habit of the Home screens: the brand panel has no control, and a screen-reader user should not hear the marketing text before the form on every sign-in.
2. Exactly one `h1`, "Sign in". The tagline is a paragraph, not a heading. There is no `h2`. The tab title is "Sign in - {appName}" (ER-16).
3. `<html lang>` follows the chosen language; the language names in the menu carry their own `lang` attribute (`zh`, `ms`, `en`) so a screen reader pronounces them correctly.
4. The existing skip link is first and jumps to `main`.

### 6.2 Form semantics

1. Each field has a visible `<label>` (not a placeholder); there are no placeholders on this page.
2. Required marker (UX 3.3, FR-06): label text, a space, a red asterisk (`aria-hidden`), a visually hidden word "required", and `required`/`aria-required` on the control. Accessible names: "Email or username required" and "Password required" (the word once). The legend "* means required" is above the first field, `aria-hidden`, muted, at least 4.5:1, asterisk in `danger`.
3. Field errors: `aria-invalid="true"` and `aria-describedby` to the message id. The message has no `role` of its own (focus moves to the field, which reads its description).
4. Banner errors: `role="alert"`, not linked to a field. The session-expired banner: `role="status"`.
5. The caps-lock hint is linked to the password field with `aria-describedby` and is not a live region.
6. The show-password button: name "Show password", `aria-pressed`, `type="button"`.
7. Menus: the buttons have `aria-haspopup="menu"` and `aria-expanded`; items are `menuitemradio` with `aria-checked`; Arrow keys move, Enter and Space choose, Esc closes and returns focus to the button, Tab closes. On phone the sheet is a native `<dialog>` with a visible close button (existing sheet rules).

### 6.3 Screen-reader script (what a tester should hear)

| Moment | Heard |
|---|---|
| Page load (NVDA, Firefox) | "Sign in - IMS", then focus in "Email or username, required, edit text" |
| Empty submit | focus on "Email or username, required, invalid entry, Enter your email or username." |
| Wrong password | "Email, username or password is wrong." once (alert) |
| Third wrong attempt | the alert reads both sentences once |
| Session expired | "Your session expired. Sign in again." (via `aria-describedby` on the focused password or username field, once) |
| Language changed | the button name reads again as "Language: Bahasa Melayu" after focus returns |

### 6.4 Contrast (light and dark)

All pairs come from the visual contrast table (2.3); no new colour is introduced.

| Element | Pair | Light | Dark |
|---|---|---|---|
| Body, `h1`, labels | `text` on `surface` | 17.48 | 14.74 |
| Subtitle, legend, access line, version | `text-muted` on `surface` | 7.77 | 8.24 |
| Asterisk | `danger` on `surface` | 6.54 | 7.93 |
| Field boundary | `border-strong` on `surface` | 4.72 | 5.40 |
| Error banner text | `on-danger-soft` on `danger-soft` | 9.19 | 11.47 |
| Error banner icon and edge | `danger` on `danger-soft` | 5.68 | 7.17 |
| Warning and status banner text | `on-warning-soft` on `warning-soft` | 9.88 | 11.38 |
| Primary button | `on-accent` on `accent` | 7.57 | 8.64 |
| Focus ring | `focus` on `surface`, `accent-soft`, `danger-soft`, `warning-soft` | 6.31 to 7.57 | 7.27 to 9.18 |
| Brand tagline | `on-accent-soft` on `accent-soft` | 9.74 | 10.56 |
| Brand points | `text` on `accent-soft` | 14.57 | 11.68 |
| Brand check icons | `accent` on `accent-soft` | 6.31 | 6.63 |
| Brand version | `text-muted` on `accent-soft` | 6.48 | 6.53 |
| Logo tile and glyph | `on-accent` on `accent` (non-text, needs 3) | 7.57 | 8.64 |

### 6.5 Target sizes

Every control on the page is at least 44px high and wide on a phone (fields 48, Sign in 56, header buttons 44, the show-password hit area 44, menu rows 48). On desktop with a fine pointer the header buttons are 40px high (24px is the WCAG 2.2 minimum) and the field and button heights follow the density tokens (comfortable on this page; density never applies to the sign-in page, UX 5.2).

## 7. Acceptance criteria (decision 7)

A tester can verify each line. Prefix LG = login.

### 7.1 Layout and brand

| ID | Criterion |
|---|---|
| LG-01 | At 1280x720 the page shows two full-height regions: the brand panel on the left (tagline "Know what is on every shelf", three points) and the form on the right. There is no card, no box shadow on the form and no grey canvas visible around it. |
| LG-02 | At 1023px the brand panel and the points are gone from the DOM reading order (not only hidden by colour); at 1024px they are present. |
| LG-03 | At 768x1024 the page is one column, the tagline is visible under the lockup, the points are not. At 360x640 neither the tagline nor the points are shown; the lockup, `h1`, fields and button are visible without scrolling. |
| LG-04 | The lockup is a symbol plus the text from `app.name`. Changing the catalog value for `app.name` changes the name on the page without editing an image. The tab title is "Sign in - {name}". |
| LG-05 | Language and appearance buttons are in the header row at every width; at 360px with the keyboard open they are still visible after scrolling to the top, and the Sign in button is reachable. |
| LG-06 | Footer shows "Version x.y.z" matching the build. There is no privacy or terms link. |

### 7.2 Copy and errors

| ID | Criterion |
|---|---|
| LG-10 | Every string in section 3 appears exactly as written (en); none contains "please" or "!"; all use sentence case. |
| LG-11 | Empty submit: both messages "Enter your email or username." and "Enter your password." appear, focus is on the username, and no banner is visible. |
| LG-12 | Wrong password: exactly one `role="alert"` on the page with "Email, username or password is wrong.", URL stays `/login`, username kept, no toast. |
| LG-13 | After LG-12, typing one character in either field removes the banner. Submitting both fields empty after a banner shows only the two field messages. Reloading the page shows neither. |
| LG-14 | Third consecutive wrong attempt adds the second sentence "Repeated wrong attempts lock the account for a while. Ask your administrator if you are not sure of your details." The first and second attempts do not. A successful sign-in or reload resets the count. |
| LG-15 | A 401 with code `account_locked` and `retryAfterMinutes` of 15 shows "Too many failed attempts. Try again in 15 minutes."; with 1 shows "... in 1 minute."; in Chinese and Malay the wait is in the same language (not the English API sentence). When the unlock action ships, the second line "Or ask your administrator to unlock the account." is present. |
| LG-16 | A 429 with `retryAfterSeconds` of 42 shows "Too many sign-in attempts from this network. Try again in 42 seconds."; 120 shows "... in 2 minutes."; there is exactly one alert (no toast); the Sign in button stays enabled. |
| LG-17 | Browser offline before submit: a status banner "You're offline. Connect to the internet to sign in." appears; going online removes it. A submit that cannot reach the server shows "Can't reach the server. Check your connection and try again." as the single alert. |
| LG-18 | A 500 response shows "Sign-in is not working right now. Try again in a moment." as the single alert. |
| LG-19 | Changing the language while a banner is shown rebuilds the banner in the new language and keeps the typed values. |

### 7.3 Behaviour

| ID | Criterion |
|---|---|
| LG-20 | On load the username field has focus (desktop and phone). With `reason=expired` and a username kept in memory, the username is prefilled and the password has focus. |
| LG-21 | Enter in the username or password field submits once; with a delayed server, a second Enter or click sends no second request; the button has `aria-busy="true"` and `aria-disabled="true"`, the fields are `readonly`, and focus stays where it was. |
| LG-22 | After 8 seconds of waiting the line "Still signing in. This is taking longer than usual." appears. |
| LG-23 | Show password: toggles the field between hidden and visible, `aria-pressed` follows, the label stays "Show password", focus stays in the password field when pressed with a pointer, the typed value is unchanged, and the field is hidden again after a submit and after the tab is hidden. |
| LG-24 | Caps lock: a key event reporting Caps Lock on, with the password field focused and hidden, shows "Caps Lock is on."; it disappears on blur and while the password is revealed. |
| LG-25 | The username has `autocomplete="username"`, `autocapitalize="none"`, `spellcheck="false"`, `inputmode="email"` and is not `type="email"`; the password has `autocomplete="current-password"`; the fields are inside one `<form>` with a submit button; no `autocomplete="off"` anywhere on the page; paste works. |
| LG-26 | After a successful and after a failed sign-in, `localStorage` and `sessionStorage` contain no username, no password and no token; `localStorage` has only the language key and the device theme key. |
| LG-27 | Language: choosing 中文 changes the `h1` to "登录", `<html lang>` to `zh`, keeps typed values and survives a reload; the same for Bahasa Melayu and back to English. The menu works by keyboard (arrows, Enter, Esc returns focus to the button). |
| LG-28 | Appearance: choosing Dark on the sign-in page sets the dark theme at once, survives a reload without a light flash, and after signing in the user is still in dark. "Use device setting" follows the emulated colour scheme live. |
| LG-29 | Session expiry: a signed-in user on `/receipts` whose session is revoked ends on the sign-in page with the status banner and, after signing in again, on `/receipts`. `returnUrl=//evil.example` and `returnUrl=/login` end on Home. |
| LG-30 | Dark mode: axe finds 0 serious or critical issues on the sign-in page in light and dark, in English and Chinese; a screenshot review confirms the brand panel, banner, menu and focus ring are legible in dark. |

### 7.4 Responsive and accessibility

| ID | Criterion |
|---|---|
| LG-40 | At 320x568, and at 1280 wide with 400% browser zoom, there is no horizontal page scroll (`scrollWidth` equals `clientWidth`), no clipped or overlapped control, and the Sign in button can be reached and pressed. |
| LG-41 | At 360x640 every interactive element is at least 44px high and wide (measured), the Sign in button is at least 56px high, fields 48px. |
| LG-42 | With text size set to 200% and the text-spacing override applied, no text is clipped or overlaps. |
| LG-43 | Landmarks: one `banner`, one `main`, one `contentinfo`, and at >= 1024px one labelled `complementary`; exactly one `h1` "Sign in"; DOM order header, main, aside, footer. |
| LG-44 | Accessible names: username "Email or username required", password "Password required" (the word once); the asterisk is `aria-hidden`; the legend "* means required" is present once, above the first field. |
| LG-45 | Tab order: skip link, language, appearance, username, password, show password, Sign in; Shift+Tab reverses it; no positive `tabindex`. |
| LG-46 | Forced colours (`forcedColors: 'active'`): field, button, banner and header-button borders are visible, the brand panel has a visible edge, the logo is recognisable, focus is visible. |
| LG-47 | Reduced motion (`reducedMotion: 'reduce'`): `document.getAnimations()` is empty after load and while idle; the busy spinner is replaced by the static icon and the word "Working". |
| LG-48 | The page has no web font, no raster image and no request other than the app files and the config; first contentful paint is not slower than today (compare the production build). |

### 7.5 Playwright scenario to add

New file `e2e/tests/25-login.spec.ts` (number 25 is new: the plan reserves 14 to 24; the plan v2 should reserve 25). Use the existing fixtures (`login`, `signIn`, `api`, `shell`) and the login budget helper (`spendLoginBudget`). To protect the shared rate limit (20 per minute per address, shared with token refresh) every case that needs a failure, a lockout, a 429, a 500 or no network stubs the sign-in response with `page.route` and does not spend real attempts. Exactly one case uses a real wrong password (it already exists in `08-session`); one case signs in for real.

Cases (title style as in the other files: given / when / then):

1. Layout, desktop: at 1280x720 the banner, main, complementary and contentinfo landmarks exist; `h1` "Sign in"; the tagline and three points are visible; no horizontal scroll. (LG-01, 04, 43)
2. Layout, tablet and phone: at 768x1024 the points are absent and the tagline visible; at 360x640 neither is visible, controls measure at least 44px and the button at least 56px. (LG-02, 03, 41)
3. Reflow: at 320x568 and at an emulated 400% zoom (viewport 320 wide) `scrollWidth` equals `clientWidth` and the button is reachable. (LG-40)
4. Autofocus and Enter: username focused on load; typing both values and pressing Enter in the password field signs in once. (LG-20, 21)
5. Empty submit: two messages, focus on the username, no banner, `aria-invalid` set; accessible names with "required" once; legend once. (LG-11, 44)
6. Wrong credentials (real server, one attempt): one alert with the exact sentence, URL `/login`, no toast, username kept; typing clears it; empty submit after it shows only field messages; reload shows nothing. (LG-12, 13)
7. Repeat hint (stubbed 401 three times): second sentence appears on the third only; a reload resets. (LG-14)
8. Lockout (stubbed 401 `account_locked` with `retryAfterMinutes` 15 and 1): the sentence with the wait, in English, then in Chinese after switching language with the banner shown. (LG-15, 19)
9. Rate limit (stubbed 429 with `retryAfterSeconds` 42 and 120, plus an empty-seconds case): sentence, one alert, button enabled. (LG-16)
10. Offline and network: `context.setOffline(true)` shows the status banner, submit shows the network alert, `setOffline(false)` clears the status banner; stubbed 500 shows the server sentence. (LG-17, 18)
11. Busy: delay the stubbed sign-in by 1.5 s; the button shows `aria-busy`/`aria-disabled`, the fields are read-only, a second click sends no second request (count requests), then Home loads. (LG-21)
12. Show/hide and caps lock: toggle changes the input type, `aria-pressed`, focus stays in the field, the value is kept, hides again after submit; a dispatched key event with `modifierCapsLock` shows the hint and blur hides it. (LG-23, 24)
13. Password manager attributes and no storage: attribute list on both fields, one `<form>`, no `autocomplete="off"`; after a real sign-in the storage keys are only language and device theme. (LG-25, 26)
14. Language: switch to 中文, Bahasa Melayu, English from the menu with the keyboard; `h1`, `<html lang>`, tab title and typed values; reload keeps it. (LG-27)
15. Appearance: choose Dark from the menu; reload shows dark at `domcontentloaded`; "Use device setting" follows `page.emulateMedia({ colorScheme })`; after sign-in the user is dark. (LG-28)
16. Session expiry and return URL: sign in as staff, open `/receipts`, revoke the session (clear the cookie and trigger a call), land on `/login` with the status banner and prefilled username, password focused, sign in, back on `/receipts`; `returnUrl=//evil.example` and `returnUrl=/login` end on Home. (LG-29, 20)
17. Accessibility: axe scan on the page in light and dark, in English and Chinese, 0 serious or critical; Tab order matches LG-45; forced-colors and reduced-motion checks (LG-46, 47). Extend `11-accessibility` with the sign-in page in both colour schemes as well.

Page object changes (in the same PR as the redesign, by the slice author, as the plan's rule for `LoginPage` says): add `heading`, `usernameMessage`, `passwordMessage`, `banner` (alert), `status` (status), `showPassword` (button "Show password"), `languageButton` (name starts with "Language"), `appearanceButton` (name starts with "Appearance"), `capsHint`, `accessLine`, `version`. Keep `username` (`^Email or username`), `passwordBox` (`^Password`), `submit` ("Sign in") and `error` (alert) working: the new accessible names still start with the same words. The existing `08-session` case for the wrong password stays and is updated for the banner rules.

## 8. Where the plan v2 should reference this file

The architect is writing plan v2 in parallel; this file does not edit it. Please reference this file in these places:

| Plan place | What to reference |
|---|---|
| Slice table (section 1) | A new slice, suggested name "R25 Sign-in page". Scope: sections 1 to 6 of this file. Depends on R01 (tokens), R02 (required marker and legend), R05 (`monitor` icon, tokens), R06 (theme device copy and `theme-init.js`), R08 (button busy and field states), R11 (the login call flagged so it raises no toast), R13 (banner and sheet look). Size estimate about 900 lines. Wave: after wave 5. |
| R02 text | Its "Login rules (D3)" are kept; the page-level behaviour moves here (4.3, 4.4). R02 still ships the required marker and legend first. Add that the banner data is stored as kind and numbers (4.4 item 6). |
| Rulings table (section 0) | A ruling for the theme choice made on the sign-in page (section 4.7): adopted as the signing-in user's preference. It adds one rule to C5; it does not contradict the sign-out reset. |
| R06 | Login page reads and writes the device copy; `theme-init.js` already covers first paint. |
| R11 | The sign-in call is one of the "handled inline" calls that must not toast. |
| Reserved e2e files (2.3) | Add `25-login` (section 7.5). Updated existing: `08-session`, `11-accessibility`. |
| Backend table (section 3) | B7: administrator unlock of a locked account (an action on the Users screen and endpoint that clears `LockoutUntil` and `FailedLoginCount`, audited, permission `users.manage`). Needed for the second lockout line (4.4). B8 (optional): `GET`-able lockout policy values are not needed; the number comes from `retryAfterMinutes`. |
| Re-slice for ER-01 | A further slice "Re-sign-in in place" (section 4.11), after R25; Should priority. |
| Brand assets | `favicon.svg`, `icons/icon.svg` and the PWA icon PNGs recoloured to the new accent with the same geometry (section 2.2); a note for R01/R06 owner (manifest colours are already in R06). |
| Risks (section 5) | New: K16 the sign-in rate limit is per address and shared with refresh (the copy handles it; a per-account-and-address limiter is SEC-08's fix and is not in this refresh). |

## 9. Hand-offs by role

| Role | Needs to do |
|---|---|
| fe-designer | Draw `logo-tile.svg` and `logo-glyph.svg` from 2.2; recolour favicon and app icons; confirm the brand panel look in light and dark with the existing tokens (1.5); screenshots at 320, 360, 768, 1280 and 1920 in both themes for the wave gate; confirm leading icons (1.4 note). If the pale panel is judged too weak after the screenshots, the answer is a new `brand` token pair added to the contrast script in the same PR, decided by fe-designer, not a return to the owner. |
| fe-architect | Slice R25 and the re-sign-in slice; contract for the language and appearance menus (a menu component, `menuitemradio`, sheet on phone); where `lastUsername` lives in memory (`core/identity`); the build-time `APP_VERSION`; error kind model for the login call (data, not strings); the login call flag for the error interceptor. |
| fe-framework | Build per this file; keep `e2e` page object compatible. |
| security-engineer | Confirm: no remembered username in storage, in-memory username after expiry only; the 429 and lockout copy leaks nothing new; the lockout message only for the right password (unchanged); B7 unlock permission and audit; CSP unaffected (no inline script, no new origin). |
| be-framework / be-architect | B7 (unlock). Nothing else is needed: `retryAfterMinutes` and `retryAfterSeconds` are already returned. |
| behavior-test-engineer | `25-login.spec.ts` and the page object (7.5). |
| qa-novice, qa-edge | Re-check: sign-in by phone with a screen reader and gloves-size targets; lockout and 429 copy; ER-01, ER-11, ER-16, EA-13, EA-24, EA-26 closed. |

## 10. What this file does not decide, and the questions left

Not decided here (other lanes): the exact accent hue (visual Q1), the token values, the build mechanism of the version constant, the backend unlock implementation, and a self-service password reset (not in the PRD).

Questions only the product owner can answer (business, not taste):

1. Is there a privacy notice, terms of use or a company logo and name that must appear on the sign-in page? None exists in the repo today, so the page shows only the IMS name and version. If one exists, send the text or link and it goes in the footer.
2. Is it acceptable to promise "Made for barcode scanners and phones" on the sign-in page, knowing that in this release scanning means keyboard-type scanners in the product picker, and stocktake and offline use are later phases? If not, point 3 is reworded to "Made for phones and tablets".
