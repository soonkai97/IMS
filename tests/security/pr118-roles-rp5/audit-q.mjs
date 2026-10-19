import fs from 'node:fs';
import { call } from './lib.mjs';
const T = JSON.parse(fs.readFileSync((process.env.RV_OUT + '/tokens.json'), 'utf8'));
for (const q of ['action=role.deleted&pageSize=3', 'entityType=RolePermission&pageSize=2', 'action=user.updated&pageSize=1']) {
  const a = await call('GET', '/api/audit?' + q, T.admin);
  console.log(q, a.status, JSON.stringify((a.json.items ?? []).map(i => ({ action: i.action, entityType: i.entityType, entityId: i.entityId, actor: i.actor, before: i.before, after: i.after }))).slice(0, 900));
}
