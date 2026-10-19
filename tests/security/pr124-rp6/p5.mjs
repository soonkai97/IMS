// P04: the approval rule write without roleRequired; old clients; gating; audit.
import { call, login, expect, failures, sql, short } from './lib.mjs';
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const T = {};
for (const u of ['admin', 'owner', 'supervisor', 'clerk', 'staff']) T[u] = (await login(u)).token;
const before = (await call('GET', '/api/approval-rules', T.admin)).json;
console.log('rules before', before.length, JSON.stringify(before[0] ?? {}).slice(0, 200));
const post = (b, who = 'admin') => call('POST', '/api/approval-rules', T[who], b);
const base = { docType: 'Receipt', qtyThreshold: 777, isActive: false };

expect('old client: roleRequired only -> 400', await post({ ...base, roleRequired: 'Owner' }), 400);
expect('old client: roleRequired Admin only -> 400', await post({ ...base, roleRequired: 'Admin' }), 400);
expect('no tier at all -> 400', await post({ ...base }), 400);
expect('tier null -> 400', await post({ ...base, approverTier: null }), 400);
expect('tier 99 -> 400', await post({ ...base, approverTier: 99 }), 400);
expect('tier negative -> 400', await post({ ...base, approverTier: -1 }), 400);
expect('tier text junk -> 400', await post({ ...base, approverTier: 'Everyone' }), 400);
const tiers = {};
for (const t of [0, 1, 'Standard', 'Large', 'standard', 'large']) { const r = await post({ ...base, approverTier: t }); console.log('tier', JSON.stringify(t), '->', short(r), r.json?.approverTier ?? '', r.json?.roleRequired ?? ''); tiers[t] = r.json; }
// roleRequired contradicting the tier is ignored, the tier decides (no widening or narrowing from the text)
const a = await post({ ...base, qtyThreshold: 778, approverTier: 'Large', roleRequired: 'Clerk' });
const b = await post({ ...base, qtyThreshold: 779, approverTier: 'Standard', roleRequired: 'Admin' });
console.log('Large+roleRequired Clerk ->', short(a), a.json?.approverTier, a.json?.roleRequired, '| Standard+roleRequired Admin ->', short(b), b.json?.approverTier, b.json?.roleRequired);
expect('old client PUT with roleRequired and tier keeps the tier', await call('PUT', `/api/approval-rules/${a.json.id}`, T.admin, { docType: 'Receipt', qtyThreshold: 778, isActive: false, approverTier: 'Standard', roleRequired: 'Owner' }), 200);
const after = (await call('GET', `/api/approval-rules`, T.admin)).json.find(r => r.id === a.json.id);
console.log('rule after PUT: tier', after.approverTier, 'roleRequired(shown)', after.roleRequired);
expect('PUT old client roleRequired only -> 400 and the rule is unchanged', await call('PUT', `/api/approval-rules/${a.json.id}`, T.admin, { docType: 'Receipt', qtyThreshold: 778, isActive: false, roleRequired: 'Owner' }), 400);
console.log('tier unchanged:', (await call('GET', `/api/approval-rules`, T.admin)).json.find(r => r.id === a.json.id).approverTier === after.approverTier);
// gating
for (const u of ['owner', 'supervisor', 'clerk', 'staff']) expect(`${u} creates a rule -> 403`, await post({ ...base, qtyThreshold: 780, approverTier: 'Standard' }, u), 403);
expect('anonymous -> 401', await call('POST', '/api/approval-rules', null, { ...base, approverTier: 'Standard' }), 401);
// mass assignment on the rule
const m = await post({ ...base, qtyThreshold: 781, approverTier: 'Standard', id: '00000000-0000-0000-0000-000000000001', isSystem: true, recDeleted: true });
console.log('over-post ->', short(m), 'id kept server side:', m.json?.id !== '00000000-0000-0000-0000-000000000001');
// audit: the rule writes leave rows and events
console.log('audit rows for ApprovalRule in the last 5 minutes:', sql("SELECT COUNT(*) FROM AuditLogs WHERE EntityType='ApprovalRule' AND REC_CREDATE > DATEADD(minute,-5,SYSUTCDATETIME())"), 'events:', sql("SELECT Action+':'+CAST(COUNT(*) AS varchar(9)) FROM AuditLogs WHERE EntityType='ApprovalRule' AND REC_CREDATE > DATEADD(minute,-5,SYSUTCDATETIME()) GROUP BY Action").replace(/s+/g,' '));
console.log('failures', failures());
