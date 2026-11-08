import { SalesOrderDto, SalesOrderLineDto, SalesOrderStatus } from '@ims/api';
import { LineValue } from '@ims/features/documents';
import { headerFromOrder, lineFromOrderLine, toSaveRequest } from './so-form';

/** A line as the API sends it to a holder of so.manage; what the line does not have is absent or null. */
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
  remark: null,
  deliveredQty: 0,
  remainingQty: 12,
  deliveredBaseQty: 0,
  remainingBaseQty: 144
} as unknown as SalesOrderLineDto;

const ORDER = {
  id: 'o1',
  status: SalesOrderStatus.Draft,
  customerId: 'c1',
  warehouseId: 'w1',
  remark: 'Deliver to bay 3',
  lines: [LINE]
} as unknown as SalesOrderDto;

describe('lineFromOrderLine', () => {
  it('keeps the unit it was entered in, its factor and the price; a null remark is empty', () => {
    const value = lineFromOrderLine(LINE);

    expect(value).toMatchObject({ qty: 12, uomId: 'carton', unitCost: 5.5, remark: '' });
    expect(value.uomOptions).toEqual([{ uomId: 'carton', code: 'CTN', factorToBase: 12 }]);
  });
});

describe('toSaveRequest', () => {
  const line = (over: Partial<LineValue> = {}): LineValue => ({
    ...lineFromOrderLine(LINE),
    ...over
  });

  it('a blank price is sent as 0 and a remark of blanks as none', () => {
    const body = toSaveRequest({ customerId: 'c1', warehouseId: 'w1', remark: '   ' }, [
      line({ unitCost: null }),
      line({ uomId: 'pcs', unitCost: 0.5 })
    ]);

    expect(body.remark).toBeUndefined();
    expect(body.lines.map(row => row.unitPrice)).toEqual([0, 0.5]);
  });

  it('an order loaded and saved with only the remark changed sends the whole body: customer, warehouse, every line field', () => {
    const header = { ...headerFromOrder(ORDER), remark: ' New remark ' };

    expect(toSaveRequest(header, [lineFromOrderLine(LINE)])).toEqual({
      warehouseId: 'w1',
      customerId: 'c1',
      remark: 'New remark',
      lines: [{ productId: 'p1', qty: 12, uomId: 'carton', unitPrice: 5.5, remark: undefined }]
    });
  });
});
