import { Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ReorderSuggestion } from '@ims/api';
import { MAX_PREFILL_LINES, parsePrefillLines } from '../form/po-form';
import { buildReorderEntryLink, isValidReorderQty, reorderValue } from './reorder';

const WAREHOUSE = '00000000-0000-4000-8000-0000000000aa';
const SUPPLIER = '00000000-0000-4000-8000-0000000000bb';

const ROW: ReorderSuggestion = {
  productId: '00000000-0000-4000-8000-000000000001',
  sku: 'SKU-1',
  productName: 'Widget',
  uomCode: 'PCS',
  warehouseId: WAREHOUSE,
  warehouseCode: 'MAIN',
  warehouseName: 'Main warehouse',
  qtyOnHand: 3,
  safetyStock: 10,
  onOrderQty: 0,
  suggestedQty: 17,
  suggestedValue: 34
};

describe('isValidReorderQty', () => {
  it.each([1, 0.0001, 12.5, 1_000_000_000])('accepts %p', qty => {
    expect(isValidReorderQty(qty)).toBe(true);
  });

  it.each([0, -1, 0.00001, 1.23456, 1_000_000_001, null, undefined, NaN])('refuses %p', qty => {
    expect(isValidReorderQty(qty as number | null)).toBe(false);
  });
});

describe('buildReorderEntryLink', () => {
  it('names the warehouse, the supplier and one line per row, in the order given', () => {
    const link = buildReorderEntryLink(WAREHOUSE, SUPPLIER, [
      { sku: 'SKU-1', qty: 12 },
      { sku: 'SKU-2', qty: 0.5 }
    ]);

    expect(link.commands).toEqual(['/', 'purchase-orders', 'new']);
    expect(link.queryParams).toEqual({
      warehouseId: WAREHOUSE,
      supplierId: SUPPLIER,
      line: ['SKU-1|12', 'SKU-2|0.5']
    });
    expect(link.omitted).toBe(0);
  });

  it('drops a line the editor would refuse and says nothing about it twice', () => {
    const link = buildReorderEntryLink(WAREHOUSE, SUPPLIER, [
      { sku: 'A', qty: 0 },
      { sku: '', qty: 3 },
      { sku: 'B', qty: 1.23456 },
      { sku: 'C', qty: 4 }
    ]);

    expect(link.queryParams.line).toEqual(['C|4']);
  });

  it('sends at most the lines the editor takes and counts the rest', () => {
    const lines = Array.from({ length: MAX_PREFILL_LINES + 7 }, (_, index) => ({
      sku: `S${index}`,
      qty: 1
    }));

    const link = buildReorderEntryLink(WAREHOUSE, SUPPLIER, lines);

    expect(link.queryParams.line).toHaveLength(MAX_PREFILL_LINES);
    expect(link.omitted).toBe(7);
  });

  it('survives the address bar: codes with & # % = | + spaces and non-Latin letters come back as typed', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const router = TestBed.inject(Router);
    const codes = ['A&B=C', 'X#1', '100%', 'a|b', 'P+Q R', '螺丝-1', 'é/ü?'];
    const link = buildReorderEntryLink(
      WAREHOUSE,
      SUPPLIER,
      codes.map((sku, index) => ({ sku, qty: index + 1.5 }))
    );

    const url = router.serializeUrl(
      router.createUrlTree([...link.commands], { queryParams: link.queryParams })
    );
    const parsed = router.parseUrl(url);

    expect(parsed.queryParams['warehouseId']).toBe(WAREHOUSE);
    expect(parsed.queryParams['supplierId']).toBe(SUPPLIER);
    expect(parsed.queryParamMap.getAll('line')).toHaveLength(codes.length);
    expect(parsePrefillLines(parsed.queryParamMap.getAll('line'))).toEqual(
      codes.map((sku, index) => ({ sku, qty: index + 1.5 }))
    );
  });
});

describe('reorderValue', () => {
  it('is the server figure while the quantity is the suggested one', () => {
    expect(reorderValue(ROW, 17)).toBe(34);
  });

  it('scales to the quantity in the box, to the cent', () => {
    expect(reorderValue(ROW, 5)).toBe(10);
    expect(reorderValue({ ...ROW, suggestedValue: 10 }, 1)).toBe(0.59);
  });

  it('is null, never 0, when the server sent none (no cost.view, never stocked)', () => {
    expect(reorderValue({ ...ROW, suggestedValue: null as unknown as undefined }, 17)).toBeNull();
    expect(reorderValue({ ...ROW, suggestedValue: undefined }, 5)).toBeNull();
  });

  it('is null while the box holds no valid quantity', () => {
    expect(reorderValue(ROW, null)).toBeNull();
    expect(reorderValue(ROW, 0)).toBeNull();
  });
});
