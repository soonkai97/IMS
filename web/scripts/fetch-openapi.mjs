// Downloads the API's OpenAPI document and writes two files:
//   openapi/openapi.raw.json         exactly what the server returned (for diffing against API changes)
//   openapi/openapi.normalized.json  the same document in the OpenAPI 3.0 flavour NSwag 14 understands
// The server document is correct at the source (string enums, operationIds `Tag_action`, binary responses, inline
// multipart files, typed numbers). Only two things are left to adapt here, both because NSwag 14 reads OpenAPI 3.0:
//   1. 3.1 nullability (`type: ["string","null"]`, `oneOf: [{type: null}, {$ref}]`) becomes `nullable: true`;
//   2. generated interfaces get no `[key: string]: any` index signature (`additionalProperties: false`).
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const url = process.argv[2] ?? process.env.OPENAPI_URL ?? 'http://localhost:5109/openapi/v1.json';

const res = await fetch(url);
if (!res.ok) throw new Error(`GET ${url} -> ${res.status}`);
const raw = await res.text();
const doc = JSON.parse(raw);

const isNullType = s => s && s.type === 'null';

/** OpenAPI 3.1 -> 3.0 nullability rewrite. */
function fixSchema(s) {
  if (Array.isArray(s)) return s.forEach(fixSchema);
  if (!s || typeof s !== 'object') return;
  if (Array.isArray(s.type)) {
    if (s.type.includes('null')) s.nullable = true;
    s.type = s.type.find(t => t !== 'null');
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
fixSchema(doc);

for (const schema of Object.values(doc.components.schemas)) {
  if (schema.type === 'object') schema.additionalProperties = false;
}

mkdirSync(join(root, 'openapi'), { recursive: true });
writeFileSync(join(root, 'openapi', 'openapi.raw.json'), raw);
writeFileSync(join(root, 'openapi', 'openapi.normalized.json'), JSON.stringify(doc, null, 2));
console.log(
  `OpenAPI downloaded from ${url} and normalized (${Object.keys(doc.paths).length} paths).`
);
