// helper: re-create the wh2 document attachment used by the matrix if an earlier run deleted it
import { raw, login, loadState, saveState } from './lib.mjs';
import { pngBytes } from './pngbytes.mjs';
const st = loadState(); const t = await login('admin');
const f = new FormData(); f.append('ownerType', 'Document'); f.append('ownerId', st.docs.wh2Draft.id); f.append('file', new Blob([pngBytes()], { type: 'image/png' }), 'b.png');
const r = await raw('POST', '/api/attachments', { token: t, form: f }); st.att.wh2Doc = r.data.id; saveState(st); console.log(r.status);
