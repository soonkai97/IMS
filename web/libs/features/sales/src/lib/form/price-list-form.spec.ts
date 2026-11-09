import { PriceListDto, PriceListItemDto } from '@ims/api';
import {
  PriceItemValue,
  createPriceItemGroup,
  createPriceListHeader,
  duplicateItemIndexes,
  headerFromList,
  itemFromDto,
  toPriceListSaveRequest
} from './price-list-form';

const ITEM = {
  productId: 'p1',
  sku: 'SKU-1',
  productName: 'Widget',
  uomId: 'ctn',
  uomCode: 'CTN',
  price: 12.5
} as PriceListItemDto;
const LIST = {
  id: 'l1',
  name: 'Retail',
  isDefault: false,
  customerId: 'c1',
  customerName: 'Acme',
  items: [ITEM],
  concurrencyToken: 'AAAA'
} as PriceListDto;

describe('price list form', () => {
  it('a list loaded and saved with only the name changed sends the whole body', () => {
    const header = { ...headerFromList(LIST), name: '  Retail 2  ' };

    expect(toPriceListSaveRequest(header, [itemFromDto(ITEM)])).toEqual({
      name: 'Retail 2',
      isDefault: false,
      customerId: 'c1',
      items: [{ productId: 'p1', uomId: 'ctn', price: 12.5 }]
    });
  });

  it('the default list is sent with no customer, whatever the form still holds', () => {
    const body = toPriceListSaveRequest({ name: 'All', isDefault: true, customerId: 'c1' }, []);

    expect(body.customerId).toBeUndefined();
    expect(body.isDefault).toBe(true);
  });

  it('keeps the unit a price was entered in as the only unit of a saved item', () => {
    expect(itemFromDto(ITEM).uomOptions).toEqual([{ uomId: 'ctn', code: 'CTN', factorToBase: 1 }]);
  });

  it('finds the second and later item of the same product and unit, and not the same product in another unit', () => {
    const item = (productId: string, uomId: string): PriceItemValue => ({
      ...itemFromDto(ITEM),
      productId,
      uomId
    });

    expect(
      duplicateItemIndexes([item('a', 'u'), item('a', 'v'), item('b', 'u'), item('a', 'u')])
    ).toEqual(new Map([[3, 0]]));
  });

  it('asks for a name, and for a price above 0 with at most 2 decimals', () => {
    expect(createPriceListHeader().controls.name.valid).toBe(false);

    const price = createPriceItemGroup({ ...itemFromDto(ITEM), price: null }).controls.price;
    for (const [value, valid] of [
      [null, false],
      [0, false],
      [0.005, false],
      [0.01, true],
      [12.5, true],
      [12.345, false],
      [1_000_000_001, false]
    ] as [number | null, boolean][]) {
      price.setValue(value);
      expect(price.valid).toBe(valid);
    }
  });
});
