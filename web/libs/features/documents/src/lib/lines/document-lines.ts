import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  ValidationErrors,
  ValidatorFn,
  Validators
} from '@angular/forms';
import { DocumentLineDto, DocumentLineInput, DocumentType, ProductDto } from '@ims/api';
import { MAX_QUANTITY, MAX_QUANTITY_DECIMALS } from '@ims/models';
import { roundQuantity, toNumber } from '@ims/utils';

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

export const MIN_LINE_QTY = 0.001;
const DEFAULT_QTY = 1;
const BASE_FACTOR = 1;

/** The API stores at most 4 decimals; reject more instead of letting the server do it. */
export function maxDecimals(count: number): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    if (typeof value !== 'number') {
      return null;
    }
    const decimals = (String(value).split('.')[1] ?? '').length;
    return decimals > count ? { decimals: { max: count } } : null;
  };
}

export function createLineGroup(value: LineValue): LineGroup {
  return new FormGroup({
    productId: new FormControl(value.productId, { nonNullable: true }),
    sku: new FormControl({ value: value.sku, disabled: true }, { nonNullable: true }),
    productName: new FormControl(
      { value: value.productName, disabled: true },
      { nonNullable: true }
    ),
    qty: new FormControl<number | null>(value.qty, [
      Validators.required,
      Validators.min(MIN_LINE_QTY),
      Validators.max(MAX_QUANTITY),
      maxDecimals(MAX_QUANTITY_DECIMALS)
    ]),
    uomId: new FormControl(value.uomId, { nonNullable: true }),
    unitCost: new FormControl<number | null>(value.unitCost, [Validators.min(0)]),
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
    unitCost: line.unitCost > 0 ? line.unitCost : null,
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
  readonly lineCount: number;
  /** Sum of the base-unit quantities. */
  readonly totalQty: number;
  /** Receipts only: sum of qty * unit cost. */
  readonly totalAmount: number;
  /** Issues only: lines the stock does not cover. */
  readonly shortLineCount: number;
}

export function computeTotals(lines: readonly LineValue[], type: DocumentType): DocumentTotals {
  const isReceipt = type === DocumentType.Receipt;
  return {
    lineCount: lines.length,
    // A line being typed (empty, 0 or negative) adds nothing: the footer must never say "-4 units".
    totalQty: roundQuantity(lines.reduce((sum, line) => sum + Math.max(0, baseQuantity(line)), 0)),
    totalAmount: isReceipt
      ? roundQuantity(
          lines.reduce((sum, line) => sum + toNumber(line.qty) * toNumber(line.unitCost), 0),
          2
        )
      : 0,
    shortLineCount:
      type === DocumentType.Issue ? lines.filter(line => lineShortfall(line) > 0).length : 0
  };
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
