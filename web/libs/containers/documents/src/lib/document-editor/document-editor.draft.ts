import { IssuePurpose } from '@ims/api';
import { LineValue, UomOption } from '@ims/features/documents';
import {
  MAX_DOCUMENT_LINES,
  MAX_QUANTITY,
  MAX_REMARK_LENGTH,
  MAX_UNIT_COST,
  PublicId
} from '@ims/models';
import { normalizePublicId } from '@ims/utils';
import { EDITOR_DRAFT_VERSION, EditorDraft } from './document-editor.view-service';

const MAX_TEXT_LENGTH = 300;
const MAX_CODE_LENGTH = 50;
const MAX_UOM_OPTIONS = 50;
const MAX_TOKEN_LENGTH = 200;

/** A string of at most `max` characters, else undefined (the draft is then refused as a whole). */
const text = (value: unknown, max: number): string | undefined =>
  typeof value === 'string' && value.length <= max ? value : undefined;

/** A finite number within [min, max] or null; undefined when it is neither. */
function numberOrNull(value: unknown, min: number, max: number): number | null | undefined {
  if (value === null) {
    return null;
  }
  return typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max
    ? value
    : undefined;
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/** An id kept on the device is read through normalizePublicId (frontend.md decision 16); undefined when it is not a GUID. */
const idOf = (value: unknown): PublicId | undefined =>
  typeof value === 'string' ? normalizePublicId(value) : undefined;

/** Like idOf, and null stays null. */
const idOrNull = (value: unknown): PublicId | null | undefined =>
  value === null ? null : idOf(value);

function uomOptionOf(value: unknown): UomOption | undefined {
  if (!isObject(value)) {
    return undefined;
  }
  const uomId = idOf(value['uomId']);
  const code = text(value['code'], MAX_CODE_LENGTH);
  const factor = numberOrNull(value['factorToBase'], 0, Number.MAX_SAFE_INTEGER);
  return uomId && code !== undefined && typeof factor === 'number' && factor > 0
    ? { uomId, code, factorToBase: factor }
    : undefined;
}

function lineOf(value: unknown): LineValue | undefined {
  if (!isObject(value)) {
    return undefined;
  }
  const productId = idOf(value['productId']);
  const uomId = idOf(value['uomId']);
  const sku = text(value['sku'], MAX_TEXT_LENGTH);
  const productName = text(value['productName'], MAX_TEXT_LENGTH);
  const remark = text(value['remark'], MAX_REMARK_LENGTH);
  const baseUomCode = text(value['baseUomCode'], MAX_CODE_LENGTH);
  const qty = numberOrNull(value['qty'], 0, MAX_QUANTITY);
  const unitCost = numberOrNull(value['unitCost'], 0, MAX_UNIT_COST);
  const available = numberOrNull(
    value['available'],
    -Number.MAX_SAFE_INTEGER,
    Number.MAX_SAFE_INTEGER
  );
  const rawOptions = value['uomOptions'];
  if (!Array.isArray(rawOptions) || rawOptions.length > MAX_UOM_OPTIONS) {
    return undefined;
  }
  const uomOptions = rawOptions.map(uomOptionOf);
  if (
    !productId ||
    !uomId ||
    sku === undefined ||
    productName === undefined ||
    remark === undefined ||
    baseUomCode === undefined ||
    qty === undefined ||
    unitCost === undefined ||
    available === undefined ||
    uomOptions.some(option => option === undefined)
  ) {
    return undefined;
  }
  return {
    productId,
    sku,
    productName,
    qty,
    uomId,
    unitCost,
    remark,
    available,
    baseUomCode,
    uomOptions: uomOptions as UomOption[],
    // A draft is kept only for a document that belongs to no order (its prices would be cost data in storage).
    purchaseOrderLineNo: null
  };
}

/**
 * What came back from storage is untrusted (script or DevTools on this origin can write anything): rebuilt field by field, with
 * every id normalised and every size capped by what the editor itself allows. Undefined when anything is off; the draft is then
 * dropped and the notice says it was not restored.
 */
export function parseEditorDraft(raw: unknown): EditorDraft | undefined {
  if (
    !isObject(raw) ||
    raw['version'] !== EDITOR_DRAFT_VERSION ||
    !isObject(raw['header']) ||
    !Array.isArray(raw['lines'])
  ) {
    return undefined;
  }
  if (raw['lines'].length > MAX_DOCUMENT_LINES) {
    return undefined;
  }
  const header = raw['header'];
  const warehouseId = idOrNull(header['warehouseId']);
  const partnerId = idOrNull(header['partnerId']);
  const purpose = header['purpose'];
  const remark = text(header['remark'], MAX_REMARK_LENGTH);
  const basedOn = raw['basedOn'] === null ? null : text(raw['basedOn'], MAX_TOKEN_LENGTH);
  const lines = raw['lines'].map(lineOf);
  const purposeKnown =
    purpose === null || Object.values(IssuePurpose).includes(purpose as IssuePurpose);
  if (
    warehouseId === undefined ||
    partnerId === undefined ||
    !purposeKnown ||
    remark === undefined ||
    basedOn === undefined ||
    lines.some(line => line === undefined)
  ) {
    return undefined;
  }
  return {
    version: EDITOR_DRAFT_VERSION,
    header: { warehouseId, partnerId, purpose: purpose as IssuePurpose | null, remark },
    lines: lines as LineValue[],
    basedOn
  };
}
