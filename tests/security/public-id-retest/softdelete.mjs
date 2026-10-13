// Prints the sqlcmd statements that mark the "gone" rows of setup.mjs soft-deleted (REC_DELETED = 1), so the IDOR matrix can probe them.
// usage: IMS_STATE=state.json node softdelete.mjs | sqlcmd -S "(localdb)\ImsDev" -E -d <throw-away database>
import { loadState } from './lib.mjs';
const { ids } = loadState();
const q = (table, id) => `UPDATE [${table}] SET REC_DELETED = 1 WHERE PublicId = '${id}';`;
console.log(["SET QUOTED_IDENTIFIER ON;", q('User', ids.gone), q('Uom', ids.goneUom), q('Category', ids.goneCat), q('Partner', ids.gonePartner), q('Product', ids.goneProd), q('ApprovalRule', ids.goneRule), q('StockDocument', ids.goneDoc), q('Attachment', ids.goneAtt), q('Warehouse', ids.wh3), 'GO'].join('\n'));
