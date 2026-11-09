import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';
import { PriceListDto, PriceListItemDto, PriceListSaveRequest } from '@ims/api';
import { UomOption } from '@ims/features/documents';
import { PublicId } from '@ims/models';
import { MONEY_DECIMALS, maxDecimals, toNumber } from '@ims/utils';
import { MAX_PRICE_LIST_NAME } from '../constants/price-list.const';

export type PriceListHeaderGroup = FormGroup<{
  name: FormControl<string>;
  isDefault: FormControl<boolean>;
  customerId: FormControl<PublicId | null>;
}>;

export type PriceListHeaderValue = ReturnType<PriceListHeaderGroup['getRawValue']>;

/** One priced product and unit. The product's code and name are shown, never edited; the unit and the price are the person's. */
export type PriceItemGroup = FormGroup<{
  productId: FormControl<PublicId>;
  sku: FormControl<string>;
  productName: FormControl<string>;
  uomId: FormControl<PublicId>;
  uomOptions: FormControl<readonly UomOption[]>;
  price: FormControl<number | null>;
}>;

export type PriceItemsArray = FormArray<PriceItemGroup>;

export interface PriceItemValue {
  readonly productId: PublicId;
  readonly sku: string;
  readonly productName: string;
  readonly uomId: PublicId;
  readonly uomOptions: readonly UomOption[];
  readonly price: number | null;
}

export function createPriceListHeader(): PriceListHeaderGroup {
  return new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_PRICE_LIST_NAME)]
    }),
    isDefault: new FormControl(false, { nonNullable: true }),
    customerId: new FormControl<PublicId | null>(null)
  });
}

/** A price is above 0 with at most 2 decimals (the API refuses anything else). */
export function createPriceItemGroup(value: PriceItemValue): PriceItemGroup {
  return new FormGroup({
    productId: new FormControl(value.productId, { nonNullable: true }),
    sku: new FormControl(value.sku, { nonNullable: true }),
    productName: new FormControl(value.productName, { nonNullable: true }),
    uomId: new FormControl(value.uomId, { nonNullable: true }),
    uomOptions: new FormControl<readonly UomOption[]>(value.uomOptions, { nonNullable: true }),
    price: new FormControl<number | null>(value.price, [
      Validators.required,
      Validators.min(10 ** -MONEY_DECIMALS),
      maxDecimals(MONEY_DECIMALS)
    ])
  });
}

export function headerFromList(list: PriceListDto): PriceListHeaderValue {
  return { name: list.name, isDefault: list.isDefault, customerId: list.customerId ?? null };
}

/** A saved item as the form holds it. The unit it was priced in is the only one it offers (the product is not read again). */
export function itemFromDto(item: PriceListItemDto): PriceItemValue {
  return {
    productId: item.productId,
    sku: item.sku,
    productName: item.productName,
    uomId: item.uomId,
    uomOptions: [{ uomId: item.uomId, code: item.uomCode, factorToBase: 1 }],
    price: item.price ?? null
  };
}

/**
 * The whole body of a save: every field is sent every time (there is no "absent = unchanged"), the items replace the old ones, the
 * default list has no customer, and the name is sent without the blanks around it.
 */
export function toPriceListSaveRequest(
  header: PriceListHeaderValue,
  items: readonly PriceItemValue[]
): PriceListSaveRequest {
  return {
    name: header.name.trim(),
    isDefault: header.isDefault,
    customerId: header.isDefault ? undefined : (header.customerId ?? undefined),
    items: items.map(item => ({
      productId: item.productId,
      uomId: item.uomId,
      price: toNumber(item.price)
    }))
  };
}

/** The positions of items that repeat a product and unit already above them: a price is for exactly one of each. */
export function duplicateItemIndexes(items: readonly PriceItemValue[]): ReadonlySet<number> {
  const seen = new Set<string>();
  const repeated = new Set<number>();
  items.forEach((item, index) => {
    const key = `${item.productId}|${item.uomId}`;
    if (seen.has(key)) {
      repeated.add(index);
    }
    seen.add(key);
  });
  return repeated;
}
