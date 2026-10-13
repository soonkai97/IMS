import { DocumentLineDto, DocumentType, ProductDto } from '@ims/api';
import {
  LineValue,
  baseQuantity,
  buildUomOptions,
  computeTotals,
  createLineGroup,
  lineFromDocument,
  lineShortfall,
  isValidQuantity,
  maxCoveredQuantity,
  toLineInputs
} from './document-lines';

/** A GUID for a small number, so the cases stay readable. */
const id = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const PCS = { uomId: id(1), code: 'PCS', factorToBase: 1 };
const CARTON = { uomId: id(2), code: 'CTN', factorToBase: 12 };

function line(overrides: Partial<LineValue> = {}): LineValue {
  return {
    productId: id(1),
    sku: 'WIDGET-A',
    productName: 'Widget A',
    qty: 1,
    uomId: id(1),
    unitCost: null,
    remark: '',
    available: null,
    baseUomCode: 'PCS',
    uomOptions: [PCS, CARTON],
    ...overrides
  };
}

describe('document line totals', () => {
  it('sums quantities in the base unit (a carton counts as 12 pieces)', () => {
    const lines = [line({ qty: 3 }), line({ productId: id(2), qty: 2, uomId: id(2) })];

    expect(computeTotals(lines, DocumentType.Receipt).totalQty).toBe(27);
    expect(baseQuantity(lines[1])).toBe(24);
  });

  it('computes the receipt amount from quantity and unit cost, ignoring blank costs', () => {
    const lines = [
      line({ qty: 10, unitCost: 2.5 }),
      line({ productId: id(2), qty: 4, unitCost: null }),
      line({ productId: id(3), qty: 2, unitCost: 0.1 })
    ];

    const totals = computeTotals(lines, DocumentType.Receipt);

    expect(totals.lineCount).toBe(3);
    expect(totals.totalAmount).toBe(25.2);
    expect(computeTotals(lines, DocumentType.Issue).totalAmount).toBe(0);
  });

  it('NV-22: a line being typed (negative or empty quantity) adds nothing: never "-4 units"', () => {
    const lines = [
      line({ qty: -5 }),
      line({ productId: id(2), qty: null }),
      line({ productId: id(3), qty: 1 })
    ];

    expect(computeTotals(lines, DocumentType.Receipt).totalQty).toBe(1);
  });

  it('ER-12: a line the form rejects is left out of the totals line (count, quantity, amount)', () => {
    const lines = [
      line({ qty: 0.0001, unitCost: 10 }),
      line({ productId: id(2), qty: 0.00001, unitCost: 10 }),
      line({ productId: id(3), qty: 0, unitCost: 10 }),
      line({ productId: id(4), qty: null, unitCost: 10 }),
      line({ productId: id(5), qty: 2, unitCost: 10 })
    ];

    const totals = computeTotals(lines, DocumentType.Receipt);

    expect(totals.lineCount).toBe(5);
    expect(totals.validLineCount).toBe(2);
    expect(totals.totalQty).toBe(2.0001);
    expect(totals.totalAmount).toBe(20);
  });

  it('R04: the amount is the sum of line amounts rounded to cents, and skips a rejected unit cost', () => {
    const half = [
      line({ qty: 0.5, unitCost: 0.01 }),
      line({ productId: id(2), qty: 0.5, unitCost: 0.01 })
    ];
    const badCost = [
      line({ qty: 2, unitCost: 1.005 }),
      line({ productId: id(2), qty: 3, unitCost: 1.1 })
    ];

    expect(computeTotals(half, DocumentType.Receipt).totalAmount).toBe(0.02);
    expect(computeTotals(badCost, DocumentType.Receipt).totalAmount).toBe(3.3);
  });

  it('R04: the amount matches the API at the half cent (2.5 x 0.87 and 1.5 x 1.45 are 2.18 each, not 2.17)', () => {
    const lines = [
      line({ qty: 2.5, unitCost: 0.87 }),
      line({ productId: id(2), qty: 1.5, unitCost: 1.45 }),
      line({ productId: id(3), qty: 9.75, unitCost: 624.18 })
    ];

    expect(computeTotals(lines, DocumentType.Receipt).totalAmount).toBe(6090.12);
  });

  it('R04: a unit cost above the API maximum is rejected and left out of the amount', () => {
    const cost = createLineGroup(line()).controls.unitCost;

    cost.setValue(1_000_000_000);
    expect(cost.valid).toBe(true);
    cost.setValue(1_000_000_001);
    expect(cost.errors).toHaveProperty('max');
    expect(
      computeTotals([line({ qty: 1, unitCost: 1_000_000_001 })], DocumentType.Receipt).totalAmount
    ).toBe(0);
  });

  it('removes float noise (0.1 + 0.2 shows as 0.3)', () => {
    const totals = computeTotals(
      [line({ qty: 0.1 }), line({ productId: id(2), qty: 0.2 })],
      DocumentType.Receipt
    );

    expect(totals.totalQty).toBe(0.3);
  });

  it('counts the issue lines the stock does not cover', () => {
    const lines = [
      line({ qty: 30, available: 12 }),
      line({ productId: id(2), qty: 100, available: 540 }),
      line({ productId: id(3), qty: 5, available: null })
    ];

    expect(computeTotals(lines, DocumentType.Issue).shortLineCount).toBe(1);
    expect(computeTotals(lines, DocumentType.Receipt).shortLineCount).toBe(0);
  });
});

describe('shortfall', () => {
  it('is the part of the base quantity above what is on hand', () => {
    expect(lineShortfall(line({ qty: 30, available: 12 }))).toBe(18);
    expect(lineShortfall(line({ qty: 10, available: 12 }))).toBe(0);
  });

  it('converts the entered unit before comparing (2 cartons need 24 pieces)', () => {
    expect(lineShortfall(line({ qty: 2, uomId: id(2), available: 20 }))).toBe(4);
  });

  it('offers the quantity the stock covers, in the unit of the line', () => {
    expect(maxCoveredQuantity(line({ qty: 5, uomId: id(2), available: 30 }))).toBe(2.5);
    expect(maxCoveredQuantity(line({ qty: 30, available: 12 }))).toBe(12);
  });
});

describe('lines of a saved document', () => {
  const dto: DocumentLineDto = {
    productId: id(1),
    sku: 'WIDGET-A',
    productName: 'Widget A',
    qty: 2,
    uomId: id(2),
    uomCode: 'CTN',
    baseQty: 24,
    unitCost: 0,
    baseUnitCost: undefined,
    amount: undefined,
    costFromAverage: false,
    remark: undefined,
    available: 18
  };

  it('takes the stock on hand from the line, so no extra stock query is needed', () => {
    const value = lineFromDocument(dto);

    expect(value.available).toBe(18);
    expect(lineShortfall(value)).toBe(6);
  });

  it('has no availability once the server stops reporting it (posted, reversed, cancelled)', () => {
    const value = lineFromDocument({ ...dto, available: undefined });

    expect(value.available).toBeNull();
    expect(lineShortfall(value)).toBe(0);
  });

  it('has no cost to edit when the server hides it (a caller without cost.view)', () => {
    const hidden = lineFromDocument({ ...dto, unitCost: undefined });
    const known = lineFromDocument({ ...dto, unitCost: 2.5 });

    expect(hidden.unitCost).toBeNull();
    expect(known.unitCost).toBe(2.5);
    expect(toLineInputs([hidden], DocumentType.Receipt)[0].unitCost).toBeUndefined();
  });
});

describe('unit options', () => {
  it('lists the base unit and every conversion that reaches it', () => {
    const product = {
      baseUomId: id(1),
      baseUomCode: 'PCS',
      conversions: [{ fromUomId: id(2), toUomId: id(1), factor: 12 }]
    } as ProductDto;

    const options = buildUomOptions(product, new Map([[id(2), 'CTN']]));

    expect(options).toEqual([PCS, CARTON]);
  });
});

describe('request building and validation', () => {
  it('sends unit cost only on receipts and trims empty remarks', () => {
    const lines = [line({ qty: 4, unitCost: 2, remark: '  ' })];

    expect(toLineInputs(lines, DocumentType.Receipt)[0]).toMatchObject({
      qty: 4,
      unitCost: 2,
      remark: undefined
    });
    expect(toLineInputs(lines, DocumentType.Issue)[0].unitCost).toBeUndefined();
  });

  it('rejects more than 4 decimals and quantities out of range', () => {
    const group = createLineGroup(line());
    const qty = group.controls.qty;

    qty.setValue(1.23456);
    expect(qty.errors).toHaveProperty('decimals');
    qty.setValue(1.2345);
    expect(qty.valid).toBe(true);
    qty.setValue(0);
    expect(qty.errors).toHaveProperty('min');
    qty.setValue(1_000_000_001);
    expect(qty.errors).toHaveProperty('max');
    qty.setValue(null);
    expect(qty.errors).toHaveProperty('required');
  });

  it('ER-12: one quantity rule, the API\'s: 0.0001 is accepted, 0.00001 says "at most 4 decimals", 0 says "above 0"', () => {
    const qty = createLineGroup(line()).controls.qty;

    qty.setValue(0.0001);
    expect(qty.valid).toBe(true);
    qty.setValue(0.0005);
    expect(qty.valid).toBe(true);
    qty.setValue(0.00001);
    expect(Object.keys(qty.errors ?? {})[0]).toBe('decimals');
    qty.setValue(0);
    expect(Object.keys(qty.errors ?? {})[0]).toBe('min');
    qty.setValue(-1);
    expect(Object.keys(qty.errors ?? {})[0]).toBe('min');
    expect([0.0001, 1.2345, 1_000_000_000].every(isValidQuantity)).toBe(true);
    expect([null, 0, -1, 0.00001, 1_000_000_001].some(isValidQuantity)).toBe(false);
  });

  it('R04: a unit cost has at most 2 decimals and is not negative; blank is allowed', () => {
    const cost = createLineGroup(line()).controls.unitCost;

    for (const accepted of [null, 0, 10, 10.5, 10.55]) {
      cost.setValue(accepted);
      expect(cost.valid).toBe(true);
    }
    cost.setValue(12.345);
    expect(cost.errors).toEqual({ decimals: { max: 2 } });
    cost.setValue(-5);
    expect(cost.errors).toHaveProperty('min');
  });
});
