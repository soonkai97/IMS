import { PurchaseOrderLineDto } from '@ims/api';
import { LineValue } from '@ims/features/documents';
import { MAX_PREFILL_LINES, lineFromOrderLine, parsePrefillLines, toSaveRequest } from './po-form';

/** A line as the API sends it: what the line does not have is JSON null. */
const LINE = {
  lineNo: 1,
  productId: 'p1',
  sku: 'SKU-1',
  productName: 'Widget',
  qty: 12,
  uomId: 'carton',
  uomCode: 'CTN',
  baseQty: 144,
  unitPrice: 5.5,
  amount: 66,
  remark: null
} as unknown as PurchaseOrderLineDto;

describe('lineFromOrderLine', () => {
  it('keeps the unit it was entered in, its factor and the price; a null remark is empty', () => {
    const value = lineFromOrderLine(LINE);

    expect(value).toMatchObject({ qty: 12, uomId: 'carton', unitCost: 5.5, remark: '' });
    expect(value.uomOptions).toEqual([{ uomId: 'carton', code: 'CTN', factorToBase: 12 }]);
  });
});

describe('toSaveRequest', () => {
  const header = { supplierId: 's1', warehouseId: 'w1', remark: '  Deliver to bay 3 ' };
  const line = (over: Partial<LineValue> = {}): LineValue => ({
    ...lineFromOrderLine(LINE),
    ...over
  });

  it('a blank price is sent as 0 and a remark of blanks as none', () => {
    const body = toSaveRequest({ ...header, remark: '   ' }, [
      line({ unitCost: null }),
      line({ uomId: 'pcs', unitCost: 0.5 })
    ]);

    expect(body.remark).toBeUndefined();
    expect(body.lines.map(row => row.unitPrice)).toEqual([0, 0.5]);
  });
});

describe('parsePrefillLines', () => {
  it('reads SKU|quantity, one parameter or a list, and a bare code is one', () => {
    expect(parsePrefillLines('A-1|12')).toEqual([{ sku: 'A-1', qty: 12 }]);
    expect(parsePrefillLines(['A-1|12', 'B-2|0.5', 'C-3'])).toEqual([
      { sku: 'A-1', qty: 12 },
      { sku: 'B-2', qty: 0.5 },
      { sku: 'C-3', qty: 1 }
    ]);
  });

  it('drops empty parts, quantities the API would refuse, repeated codes and anything past the cap', () => {
    expect(
      parsePrefillLines([
        '',
        '|3',
        'A|0',
        'B|-1',
        'C|x',
        'E|0.00001',
        'F|1e12',
        'G|0x10',
        'H|1e2',
        'I|+3',
        'D|2',
        'D|9'
      ])
    ).toEqual([{ sku: 'D', qty: 2 }]);
    const many = Array.from({ length: MAX_PREFILL_LINES + 5 }, (_, i) => `S${i}|1`);
    expect(parsePrefillLines(many)).toHaveLength(MAX_PREFILL_LINES);
    expect(parsePrefillLines(undefined)).toEqual([]);
  });
});
