// Area 2: segregation of duties and approval tiers. Rules in force on Ims_Dev (found, not changed): purchase order Standard from RM 5,000, Large from RM 20,000, self-approval off.
import { call, tokens, fx, expect, check, failures, useAdmin, PO, act, submitted, newPo, line, approvedPo, tag } from './lib.mjs';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const L = (price, n = 1) => ({ lines: [line(F, { qty: n, unitPrice: price })] });
const sub = async (who, price, o = {}) => (await submitted(T[who], F, { ...L(price), ...o })).json;
const code = (r) => r.status;

// ---- nobody approves or rejects their own order (Administrator and Owner-like included)
for (const [who, price, other] of [['admin', 6000, 'admin2'], ['supervisor', 6000, 'sup2'], ['admin', 25000, 'owner'], ['ownerLike', 25000, 'owner']]) {
  const s = await sub(who, price);
  expect(2, `${who} cannot approve own ${s.isLarge ? 'Large' : 'Standard'} order`, await act(T[who], s.id, 'approve'), 400);
  expect(2, `${who} cannot reject own ${s.isLarge ? 'Large' : 'Standard'} order`, await act(T[who], s.id, 'reject', { reason: 'SEC self reject' }), 400);
  check(2, `${who} order still Submitted after the refused self-review`, (await call('GET', `${PO}/${s.id}`, T.admin)).json.status === 'Submitted');
  expect(2, `${other} (another holder of the tier) approves it`, await act(T[other], s.id, 'approve'), 200);
}
// ---- tier routing and boundaries
{
  const std = await sub('clerk', 6000), big = await sub('clerk', 25000);
  check(2, 'RM 6,000 routes Standard (requiresApproval, not large)', std.requiresApproval && !std.isLarge && std.status === 'Submitted');
  check(2, 'RM 25,000 routes Large', big.requiresApproval && big.isLarge);
  expect(2, 'Owner cannot approve a Standard order', await act(T.owner, std.id, 'approve'), 403);
  expect(2, 'Supervisor cannot approve a Large order', await act(T.supervisor, big.id, 'approve'), 403);
  expect(2, 'Supervisor cannot reject a Large order', await act(T.supervisor, big.id, 'reject', { reason: 'SEC tier' }), 403);
  expect(2, 'Administrator approves Standard', await act(T.admin, std.id, 'approve'), 200);
  expect(2, 'Administrator approves Large', await act(T.admin, big.id, 'approve'), 200);
  for (const [price, status, large] of [[4999.99, 'Approved', false], [5000, 'Submitted', false], [19999.99, 'Submitted', false], [20000, 'Submitted', true]]) {
    const s = await sub('clerk', price);
    check(2, `boundary RM ${price}: ${status}${large ? ' Large' : ''}`, s.status === status && s.isLarge === large, `${s.status} large=${s.isLarge}`);
    if (s.status === 'Submitted') await act(T.admin, s.id, 'approve');
  }
  const s = await sub('clerk', 25000);
  expect(2, 'a Submitted Large order cannot be edited in place down to a small one', await call('PUT', `${PO}/${s.id}`, T.clerk, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F, { unitPrice: 1 })] }, { 'If-Match': await tag(s.id) }), 409);
  const a = await approvedPo(F, T);
  expect(2, 'an Approved order cannot be edited', await call('PUT', `${PO}/${a.id}`, T.clerk, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F, { qty: 99999 })] }, { 'If-Match': await tag(a.id) }), 409);
  expect(2, 'approve an Approved order again is refused', await act(T.supervisor, a.id, 'approve'), 400, 409);
  const d = (await newPo(T.clerk, F)).json;
  expect(2, 'approve a Draft is refused', await act(T.supervisor, d.id, 'approve'), 400, 409);
  const r = await sub('clerk', 6000);
  expect(2, 'reject needs a reason of 5+ characters', await act(T.supervisor, r.id, 'reject', { reason: 'no' }), 400);
  const rj = await act(T.supervisor, r.id, 'reject', { reason: 'SEC returned' }); expect(2, 'reject with a reason returns it to Draft', rj, 200);
  check(2, 'rejected order is Draft and flagged returned', rj.json.status === 'Draft' && rj.json.isReturned === true);
  const two = await sub('clerk', 6000); await act(T.supervisor, two.id, 'approve');
  expect(2, 'second approve of the same order is not 200', await act(T.sup2, two.id, 'approve', undefined), 400, 409);
}
// ---- known gap (owner question 6): who edited a Draft may approve it if someone else submitted it. Documented, not asserted as a defect.
{
  const d = (await newPo(T.supervisor, F, L(600, 10))).json;                       // the Supervisor makes and edits the Draft
  const s = await act(T.clerk, d.id, 'submit');                                    // the Clerk submits it
  const a = await act(T.supervisor, d.id, 'approve');                              // the Supervisor who made it approves it
  check(2, `KNOWN GAP (owner question 6): creator/editor approves an order another person submitted -> ${s.status}/${a.status}`, s.status === 200 && a.status === 200, 'documented in the plan, section 11 row 6');
}
// ---- cancel and close authority
{
  const dr = (await newPo(T.clerk, F)).json;
  expect(2, 'another po.manage holder cannot cancel a Draft they did not create', await act(T.sup2, dr.id, 'cancel', {}), 403);
  expect(2, 'Supervisor (not creator) cannot cancel a Clerk Draft', await act(T.supervisor, dr.id, 'cancel', {}), 403);
  expect(2, 'the creator cancels own Draft', await act(T.clerk, dr.id, 'cancel', {}), 200);
  const dr2 = (await newPo(T.clerk, F)).json;
  expect(2, 'Administrator (modules.manage) cancels any Draft (designed, plan: departed creator)', await act(T.admin, dr2.id, 'cancel', {}), 200);
  const s = await sub('clerk', 6000);
  expect(2, 'another Clerk cannot cancel a Submitted order they did not submit', await act(T.clerk2, s.id, 'cancel', {}), 403);
  expect(2, 'the submitter cancels own Submitted order', await act(T.clerk, s.id, 'cancel', {}), 200);
  const ap = await approvedPo(F, T);
  expect(2, 'Clerk (po.manage, not a reviewer) cannot cancel an Approved order', await act(T.clerk, ap.id, 'cancel', { reason: 'SEC clerk cancel' }), 403);
  expect(2, 'Supervisor cancelling an Approved order needs a reason', await act(T.supervisor, ap.id, 'cancel', {}), 400);
  expect(2, 'Owner cannot cancel an Approved Standard-tier order', await act(T.owner, (await approvedPo(F, T, { tier: 'big' })).id, 'cancel', { reason: 'SEC owner on standard' }), 403);
  expect(2, 'Supervisor cannot cancel an Approved Large-tier order', await act(T.supervisor, (await approvedPo(F, T, { tier: 'large' })).id, 'cancel', { reason: 'SEC sup on large' }), 403);
  expect(2, 'Clerk cannot close short an Approved order with nothing received (409, cancel instead)', await act(T.clerk, ap.id, 'close', { reason: 'SEC close nothing' }), 409);
  expect(2, 'Administrator cannot close short with nothing received either', await act(T.admin, ap.id, 'close', { reason: 'SEC close nothing' }), 409);
  expect(2, 'Supervisor (reviewer) cancels an Approved order with a reason', await act(T.supervisor, ap.id, 'cancel', { reason: 'SEC reviewer cancel' }), 200);
  expect(2, 'a Cancelled order cannot be cancelled / approved / submitted again', await act(T.admin, ap.id, 'cancel', { reason: 'SEC twice' }), 400, 409);
  expect(2, 'recall by someone who did not submit is refused', await act(T.clerk2, (await sub('clerk', 6000)).id, 'recall'), 403);
}
console.log(failures() ? `${failures()} FAIL` : 'p2 all ok');
