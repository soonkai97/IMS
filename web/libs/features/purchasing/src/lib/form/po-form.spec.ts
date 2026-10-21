import { PurchaseOrderLineDto } from '@ims/api';
import { LineValue } from '@ims/features/documents';
import { lineFromOrderLine, toSaveRequest } from './po-form';

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

  it('a withheld price (null) stays no price, never 0', () => {
    expect(lineFromOrderLine({ ...LINE, unitPrice: null } as never).unitCost).toBeNull();
  });
});

describe('toSaveRequest', () => {
  const header = { supplierId: 's1', warehouseId: 'w1', remark: '  Deliver to bay 3 ' };
  const line = (over: Partial<LineValue> = {}): LineValue => ({
    ...lineFromOrderLine(LINE),
    ...over
  });

  it('sends every field of the order and of every line', () => {
    expect(toSaveRequest(header, [line({ remark: ' fragile ' })])).toEqual({
      warehouseId: 'w1',
      supplierId: 's1',
      remark: 'Deliver to bay 3',
      lines: [{ productId: 'p1', qty: 12, uomId: 'carton', unitPrice: 5.5, remark: 'fragile' }]
    });
  });

  it('a blank price is sent as 0, a remark of blanks as none, and the same product may be on two lines', () => {
    const body = toSaveRequest({ ...header, remark: '   ' }, [
      line({ unitCost: null }),
      line({ uomId: 'pcs', unitCost: 0.5 })
    ]);

    expect(body.remark).toBeUndefined();
    expect(body.lines.map(row => row.unitPrice)).toEqual([0, 0.5]);
    expect(body.lines.map(row => row.productId)).toEqual(['p1', 'p1']);
  });
});
