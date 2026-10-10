// Downloads the API's OpenAPI document and writes two files:
//   openapi/openapi.raw.json         exactly what the server returned (for diffing against API changes)
//   openapi/openapi.normalized.json  the same document made digestible for NSwag 14 (OpenAPI 3.0 flavour)
// nswag.json reads the normalized file. Every rewrite below works around a defect in the server document and is
// listed in docs/team/HANDOFFS.md for be-framework; when the server is fixed the rewrites become no-ops.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const url = process.argv[2] ?? process.env.OPENAPI_URL ?? 'http://localhost:5109/openapi/v1.json';

// The server serialises these enums as strings but publishes them as bare integers (no values).
const ENUMS = {
  DocumentStatus: ['Draft', 'Submitted', 'Approved', 'Posted', 'Reversed', 'Cancelled'],
  DocumentType: ['Receipt', 'Issue', 'Transfer', 'Adjust'],
  IssuePurpose: ['Sale', 'Use', 'Scrap'],
  LedgerReason: [
    'Receipt',
    'Issue',
    'TransferOut',
    'TransferIn',
    'Adjustment',
    'Reversal',
    'Opening'
  ],
  PartnerType: ['Supplier', 'Customer'],
  ProductSort: ['Sku', 'Name'],
  StockSort: ['Sku', 'QtyOnHand', 'Value', 'Warehouse']
};

const res = await fetch(url);
if (!res.ok) throw new Error(`GET ${url} -> ${res.status}`);
const raw = await res.text();
const doc = JSON.parse(raw);

const isNullType = s => s && s.type === 'null';

/** OpenAPI 3.1 -> 3.0 schema rewrite (type arrays, null in oneOf, number-as-string patterns). */
function fixSchema(s) {
  if (Array.isArray(s)) return s.forEach(fixSchema);
  if (!s || typeof s !== 'object') return;
  if (Array.isArray(s.type)) {
    const nonNull = s.type.filter(t => t !== 'null');
    if (s.type.includes('null')) s.nullable = true;
    // ["integer","string"] + digits pattern is how .NET 10 describes a number accepted from route/query strings.
    const t = nonNull.includes('integer')
      ? 'integer'
      : nonNull.includes('number')
        ? 'number'
        : nonNull[0];
    s.type = t;
    if (nonNull.includes('integer') || nonNull.includes('number')) delete s.pattern;
  }
  if (Array.isArray(s.oneOf) && s.oneOf.some(isNullType)) {
    s.oneOf = s.oneOf.filter(x => !isNullType(x));
    s.nullable = true;
    if (s.oneOf.length === 1 && s.oneOf[0].$ref) {
      s.allOf = [s.oneOf[0]];
      delete s.oneOf;
    }
  }
  for (const v of Object.values(s)) fixSchema(v);
}

doc.openapi = '3.0.1';
for (const [name, values] of Object.entries(ENUMS)) {
  if (doc.components.schemas[name] && !doc.components.schemas[name].enum) {
    doc.components.schemas[name] = { type: 'string', enum: values };
  }
}
fixSchema(doc);

// IFormFile is published as an empty object. NSwag only emits a FileParameter for an inline binary property.
delete doc.components.schemas.IFormFile;
const inlineFile = n => {
  if (Array.isArray(n)) return n.forEach(inlineFile);
  if (!n || typeof n !== 'object') return;
  for (const [k, v] of Object.entries(n)) {
    if (v && typeof v === 'object' && v.$ref === '#/components/schemas/IFormFile')
      n[k] = { type: 'string', format: 'binary' };
    else inlineFile(v);
  }
};
inlineFile(doc.paths);

// Generated interfaces get no `[key: string]: any` index signature (keeps hand-written code free of implicit any).
for (const schema of Object.values(doc.components.schemas)) {
  if (schema.type === 'object') schema.additionalProperties = false;
}

// Operation ids: server publishes none, so derive Tag_name (client class + method) from the route.
const BINARY = {
  content: { 'application/octet-stream': { schema: { type: 'string', format: 'binary' } } }
};
for (const [path, item] of Object.entries(doc.paths)) {
  for (const [verb, op] of Object.entries(item)) {
    if (!op || typeof op !== 'object' || !op.responses) continue;
    const segs = path.split('/').filter(Boolean).slice(2); // drop "api" and the resource
    const sub = segs.filter(s => !s.startsWith('{'));
    const hasId = segs.some(s => s.startsWith('{') && s !== '{kind}');
    const camel = s => s.replace(/-(\w)/g, (_, c) => c.toUpperCase()).replace(/\//g, '_');
    let name;
    if (sub.length) {
      name = camel(sub.join('-'));
    } else
      name =
        verb === 'get'
          ? hasId
            ? 'get'
            : 'list'
          : verb === 'post'
            ? 'create'
            : verb === 'put'
              ? 'update'
              : verb;
    if (path === '/api/import/templates/{kind}') name = 'template';
    if (path.startsWith('/api/import/') && verb === 'post')
      name = path.endsWith('opening-stock') ? 'openingStock' : 'products';
    if (path === '/api/documents/receipts') name = 'createReceipt';
    if (path === '/api/documents/issues') name = 'createIssue';
    if (path === '/api/settings' && verb === 'get') name = 'get';
    op.operationId ??= `${op.tags?.[0] ?? 'Api'}_${name}`;
    // Downloads are published without a body; declare a binary one so NSwag emits Observable<FileResponse>.
    const ok = op.responses['200'];
    if (
      verb === 'get' &&
      ok &&
      !ok.content &&
      (path.endsWith('/export') || path.includes('/templates/'))
    )
      Object.assign(ok, BINARY);
  }
}

mkdirSync(join(root, 'openapi'), { recursive: true });
writeFileSync(join(root, 'openapi', 'openapi.raw.json'), raw);
writeFileSync(join(root, 'openapi', 'openapi.normalized.json'), JSON.stringify(doc, null, 2));
console.log(
  `OpenAPI downloaded from ${url} and normalized (${Object.keys(doc.paths).length} paths).`
);
