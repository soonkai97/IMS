# QA round - supervisor/admin persona (API + source review; browser not used, see limits)

Limits: no tabs_create tool; the only browser tab ("seed") was being driven by another agent (mobile, /issues), so I did NOT drive the UI.
All behaviour below was exercised through the real API (http://localhost:5209) with demo users, plus reading web/ source. No screenshots.
QA data left behind (names start QA-): products QA-SUP-001/002; receipts/issues remarked "QA-SUP ..."; roles QA-Role-A (deleg), QA-Mgr-Role1/4;
users qa-mgr, qa-nowh, qa-allwh (inactive), qa-rpt; approval rules: 2 Receipt Large 50000 + 1 Issue qty 1000 Standard (all inactive).

## Ranked findings
1 Major  Users screen cannot assign a custom role (web users.component.ts uses fixed ALL_ROLES; list shows raw code "QARoleA").
2 Major  Owner cannot approve a Standard document; a Supervisor cannot approve their own -> with only Owner+Supervisor, a supervisor's own receipt over the Standard threshold is stuck (only Admin can approve).
3 Major  Home tiles Returned to me / My drafts / Posted today open UNFILTERED lists (no query params read by any list screen). Posted today -> /reports (250 rows) or /receipts.
4 Major  Deactivated user's access token keeps working until expiry; role edits (removing roles.manage) also apply only at next refresh (~5.5 min) - confirmed live.
5 Minor  User can be saved with no warehouse and not "sees every warehouse": signs in to an empty app (stock/docs empty, warehouse list empty).
6 Minor  Duplicate approval rules accepted silently; trigger list on the document then shows the same trigger twice.
7 Minor  API messages are English only; FE shows server detail as is, so zh/ms users get English toasts (Accept-Language ignored).
8 Minor  Movements Amount column is unsigned on reversal rows (Excel Amount sum overstates); stock effect is signed. Excel unit/avg cost shows 4 decimals; filename has no date/range.
9 Minor  Reports and Users lists have no rows-per-page; Reports has no product filter in the UI (API supports productId).
10 Minor Wording: "'Data Name' must not be empty.", "'Amount Threshold' must be greater than or equal to '0'.", rename of system role says "cannot be deleted or deactivated", 428 text says "document's version" for roles, "1 user(s)".
11 Minor  Supervisor (document.edit.any) can cancel a clerk's Submitted doc with no reason; catalog text says edit/submit but recall/edit is refused.
12 Nit   Audit details are raw JSON (PascalCase, WarehouseId:1 internal key); role rename without If-Match is allowed (lost update); role name with HTML accepted (Angular escapes).
