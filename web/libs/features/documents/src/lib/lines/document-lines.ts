import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';
import { DocumentLineDto, DocumentLineInput, DocumentType, ProductDto } from '@ims/api';
import { MAX_QUANTITY, MAX_QUANTITY_DECIMALS, MAX_UNIT_COST } from '@ims/models';
import {
  MONEY_DECIMALS,
  decimalPlaces,
  maxDecimals,
  lineAmount,
  roundMoney,
  roundQuantity,
  toNumber
} from '@ims/utils';

/** One unit a line can be entered in, with the factor that converts it to the product's base unit. */
export interface UomOption {
  readonly uomId: number;
  readonly code: string;
  readonly factorToBase: number;
}

export type UomCodes = ReadonlyMap<number, string>;

export interface LineValue {
  readonly productId: number;
  readonly sku: string;
  readonly productName: string;
  readonly qty: number | null;
  readonly uomId: number;
  readonly unitCost: number | null;
  readonly remark: string;
  /** Stock on hand in the base unit; null when unknown (receipts, or not loaded yet). */
  readonly available: number | null;
  readonly baseUomCode: string;
  readonly uomOptions: readonly UomOption[];
}

/** Typed form of one document line. The display-only members (sku, name, available...) are disabled controls. */
export type LineGroup = FormGroup<{
  productId: FormControl<number>;
  sku: FormControl<string>;
  productName: FormControl<string>;
  qty: FormControl<number | null>;
  uomId: FormControl<number>;
  unitCost: FormControl<number | null>;
  remark: FormControl<string>;
  available: FormControl<number | null>;
  baseUomCode: FormControl<string>;
  uomOptions: FormControl<readonly UomOption[]>;
}>;

export type LinesArray = FormArray<LineGroup>;

/** The smallest quantity the API takes: one unit of its last decimal (0.0001). */
export const MIN_LINE_QTY = 10 ** -MAX_QUANTITY_DECIMALS;
/** A line starts at one; a worker who never changed it has nothing to lose by removing it. */
export const DEFAULT_QTY = 1;
const BASE_FACTOR = 1;

/** The one quantity rule, the API's: from 0.0001 up to the maximum, at most 4 decimals (ER-12). */
export function isValidQuantity(qty: number | null): qty is number {
  return (
    qty !== null &&
    qty >= MIN_LINE_QTY &&
    qty <= MAX_QUANTITY &&
    decimalPlaces(qty) <= MAX_QUANTITY_DECIMALS
  );
}

/** A unit cost is money: from 0 up to the API maximum, at most 2 decimals. Blank is valid (nobody typed a cost). */
export function isValidUnitCost(unitCost: number | null): boolean {
  return (
    unitCost === null ||
    (unitCost >= 0 && unitCost <= MAX_UNIT_COST && decimalPlaces(unitCost) <= MONEY_DECIMALS)
  );
}

export function createLineGroup(value: LineValue): LineGroup {
  return new FormGroup({
    productId: new FormControl(value.productId, { nonNullable: true }),
    sku: new FormControl({ value: value.sku, disabled: true }, { nonNullable: true }),
    productName: new FormControl(
      { value: value.productName, disabled: true },
      { nonNullable: true }
    ),
    // The decimals rule comes first: 0.00001 is "at most 4 decimals", not "above 0" (ER-12).
    qty: new FormControl<number | null>(value.qty, [
      Validators.required,
      maxDecimals(MAX_QUANTITY_DECIMALS),
      Validators.min(MIN_LINE_QTY),
      Validators.max(MAX_QUANTITY)
    ]),
    uomId: new FormControl(value.uomId, { nonNullable: true }),
    unitCost: new FormControl<number | null>(value.unitCost, [
      maxDecimals(MONEY_DECIMALS),
      Validators.min(0),
      Validators.max(MAX_UNIT_COST)
    ]),
    remark: new FormControl(value.remark, { nonNullable: true }),
    available: new FormControl<number | null>({ value: value.available, disabled: true }),
    baseUomCode: new FormControl(
      { value: value.baseUomCode, disabled: true },
      { nonNullable: true }
    ),
    uomOptions: new FormControl<readonly UomOption[]>(value.uomOptions, { nonNullable: true })
  });
}

/** Units a product can be entered in: its base unit plus every conversion that reaches the base unit. */
export function buildUomOptions(product: ProductDto, codes: UomCodes): UomOption[] {
  const options: UomOption[] = [
    { uomId: product.baseUomId, code: product.baseUomCode, factorToBase: BASE_FACTOR }
  ];
  for (const conversion of product.conversions) {
    if (conversion.factor <= 0) {
      continue;
    }
    if (conversion.toUomId === product.baseUomId) {
      options.push({
        uomId: conversion.fromUomId,
        code: codes.get(conversion.fromUomId) ?? String(conversion.fromUomId),
        factorToBase: conversion.factor
      });
    } else if (conversion.fromUomId === product.baseUomId) {
      options.push({
        uomId: conversion.toUomId,
        code: codes.get(conversion.toUomId) ?? String(conversion.toUomId),
        factorToBase: BASE_FACTOR / conversion.factor
      });
    }
  }
  return options;
}

export function lineFromProduct(
  product: ProductDto,
  codes: UomCodes,
  available: number | null
): LineValue {
  return {
    productId: product.id,
    sku: product.sku,
    productName: product.name,
    qty: DEFAULT_QTY,
    uomId: product.baseUomId,
    unitCost: null,
    remark: '',
    available,
    baseUomCode: product.baseUomCode,
    uomOptions: buildUomOptions(product, codes)
  };
}

export function lineFromDocument(line: DocumentLineDto): LineValue {
  const factor = line.qty > 0 ? line.baseQty / line.qty : BASE_FACTOR;
  return {
    productId: line.productId,
    sku: line.sku,
    productName: line.productName,
    qty: line.qty,
    uomId: line.uomId,
    // Absent (null on the wire) for a caller without cost.view: the line then has no cost to edit or send.
    unitCost: (line.unitCost ?? 0) > 0 ? (line.unitCost ?? null) : null,
    remark: line.remark ?? '',
    available: line.available ?? null,
    baseUomCode: line.uomCode,
    uomOptions: [{ uomId: line.uomId, code: line.uomCode, factorToBase: factor }]
  };
}

export function factorToBase(line: Pick<LineValue, 'uomId' | 'uomOptions'>): number {
  return line.uomOptions.find(option => option.uomId === line.uomId)?.factorToBase ?? BASE_FACTOR;
}

/** Quantity of the line expressed in the product's base unit. */
export function baseQuantity(line: Pick<LineValue, 'qty' | 'uomId' | 'uomOptions'>): number {
  return roundQuantity(toNumber(line.qty) * factorToBase(line));
}

/** How much of the line the warehouse cannot cover (0 = fully covered or availability unknown). */
export function lineShortfall(line: LineValue): number {
  if (line.available === null) {
    return 0;
  }
  return Math.max(0, roundQuantity(baseQuantity(line) - line.available));
}

/** Largest quantity, in the line's own unit, that the available stock covers. */
export function maxCoveredQuantity(line: LineValue): number {
  if (line.available === null) {
    return toNumber(line.qty);
  }
  return roundQuantity(line.available / factorToBase(line));
}

export interface DocumentTotals {
  /** Every line in the list, valid or not (the "Items (n)" heading). */
  readonly lineCount: number;
  /** Lines with a valid quantity: the ones the totals line counts. */
  readonly validLineCount: number;
  /** Sum of the base-unit quantities. */
  readonly totalQty: number;
  /** Receipts only: sum of the line amounts, each `qty x unit cost` rounded to cents like the API. */
  readonly totalAmount: number;
  /** Issues only: lines the stock does not cover. */
  readonly shortLineCount: number;
}

/**
 * Totals of what the user sees. A line the form rejects (empty, zero, negative, more than 4 decimals) is left out of
 * the quantity, the count and the amount, so the footer never says "-4 units" or counts a value Save will refuse (ER-12).
 */
export function computeTotals(lines: readonly LineValue[], type: DocumentType): DocumentTotals {
  const valid = lines.filter(line => isValidQuantity(line.qty));
  return {
    lineCount: lines.length,
    validLineCount: valid.length,
    totalQty: roundQuantity(valid.reduce((sum, line) => sum + baseQuantity(line), 0)),
    totalAmount:
      type === DocumentType.Receipt
        ? roundMoney(valid.reduce((sum, line) => sum + receiptLineAmount(line), 0))
        : 0,
    shortLineCount:
      type === DocumentType.Issue ? valid.filter(line => lineShortfall(line) > 0).length : 0
  };
}

/** Receipt line amount in money. A cost the form rejects (3 decimals, negative, too large) is left out, like a blank one. */
function receiptLineAmount(line: LineValue): number {
  return isValidUnitCost(line.unitCost)
    ? lineAmount(toNumber(line.qty), toNumber(line.unitCost))
    : 0;
}

export function toLineInputs(lines: readonly LineValue[], type: DocumentType): DocumentLineInput[] {
  return lines.map(line => ({
    productId: line.productId,
    qty: toNumber(line.qty),
    uomId: line.uomId,
    unitCost: type === DocumentType.Receipt && line.unitCost !== null ? line.unitCost : undefined,
    remark: line.remark.trim().length > 0 ? line.remark.trim() : undefined
  }));
}
