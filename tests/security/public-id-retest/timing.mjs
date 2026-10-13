// Rough timing oracle check: interleaved requests for "unknown", "out of scope", "soft-deleted" and "wrong kind" ids by a warehouse-scoped user.
// A real difference (more than the noise) would let a caller tell the cases apart without reading the body. usage: IMS_STATE=state.json node timing.mjs
import { raw, login, loadState, GUID } from './lib.mjs';

const st = loadState(); const I = st.ids, D = st.docs, A = st.att;
const staff = await login('staff');
const N = 80;
const cases = {
  doc: { unknown: () => `/api/documents/${GUID()}`, outOfScope: () => `/api/documents/${D.wh2Posted.id}`, softDeleted: () => `/api/documents/${I.goneDoc}`, wrongKind: () => `/api/documents/${I.p1}`, inScope: () => `/api/documents/${D.wh1Posted.id}` },
  att: { unknown: () => `/api/attachments/${GUID()}`, outOfScope: () => `/api/attachments/${A.wh2Doc}`, softDeleted: () => `/api/attachments/${I.goneAtt}`, wrongKind: () => `/api/attachments/${D.wh1Draft.id}` },
  list: { unknown: () => `/api/attachments?ownerType=Document&ownerId=${GUID()}`, outOfScope: () => `/api/attachments?ownerType=Document&ownerId=${D.wh2Draft.id}`, softDeleted: () => `/api/attachments?ownerType=Document&ownerId=${I.goneDoc}`, wrongKind: () => `/api/attachments?ownerType=Document&ownerId=${I.p1}` },
};
const med = (a) => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; };
const p90 = (a) => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length * 0.9)]; };
for (const [group, set] of Object.entries(cases)) {
  const t = Object.fromEntries(Object.keys(set).map((k) => [k, []]));
  for (const k of Object.keys(set)) await raw('GET', set[k](), { token: staff });   // warm up
  for (let i = 0; i < N; i++) for (const k of Object.keys(set)) { const r = await raw('GET', set[k](), { token: staff }); t[k].push(r.ms); }
  console.log(group, Object.entries(t).map(([k, v]) => `${k}: median ${med(v).toFixed(1)} ms p90 ${p90(v).toFixed(1)}`).join(' | '));
}
