import { createComponentFactory, mockProvider, Spectator } from '@ngneat/spectator/jest';
import { of, throwError } from 'rxjs';
import { ProductDto, ProductLookupResult } from '@ims/api';
import { ProductsStore } from '@ims/features/catalog';
import { StockAvailabilityService } from '@ims/features/documents';
import { PickedProduct, ProductPickerComponent } from './product-picker.component';

const WIDGET = {
  id: 5,
  sku: 'W-1',
  name: 'Widget',
  isActive: true,
  baseUomCode: 'PCS'
} as ProductDto;

describe('ProductPickerComponent exact-code lookup (scanner, Enter)', () => {
  let spectator: Spectator<ProductPickerComponent>;
  const picked: PickedProduct[] = [];
  const createComponent = createComponentFactory({
    component: ProductPickerComponent,
    providers: [mockProvider(ProductsStore), mockProvider(StockAvailabilityService)]
  });
  const products = () => spectator.inject(ProductsStore);

  beforeEach(() => {
    picked.length = 0;
    spectator = createComponent();
    spectator.component.$picked.subscribe(item => picked.push(item));
  });

  function submit(code: string): void {
    spectator.component.onSubmitted(code);
    spectator.detectChanges();
  }

  it('picks the product when the code is found', () => {
    products().lookup.mockReturnValue(of<ProductLookupResult>({ found: true, product: WIDGET }));

    submit('W-1');

    expect(picked).toEqual([{ product: WIDGET, available: null }]);
    expect(spectator.component.$lookupMessage()).toBeUndefined();
  });

  it('a scan miss is an answer, not an error: a quiet message and nothing picked', () => {
    products().lookup.mockReturnValue(
      of<ProductLookupResult>({ found: false, product: undefined })
    );

    submit('NOPE-123');

    expect(picked).toEqual([]);
    expect(spectator.query('[role=status]')?.textContent).toContain(
      'No product has the code NOPE-123.'
    );
  });

  it('says so when the product is deactivated', () => {
    products().lookup.mockReturnValue(
      of<ProductLookupResult>({ found: true, product: { ...WIDGET, isActive: false } })
    );

    submit('W-1');

    expect(picked).toEqual([]);
    expect(spectator.query('[role=status]')?.textContent).toContain(
      "W-1 is deactivated and can't be added."
    );
  });

  it('a failed call (network) picks nothing and does not throw', () => {
    products().lookup.mockReturnValue(throwError(() => new Error('offline')));

    expect(() => submit('W-1')).not.toThrow();
    expect(picked).toEqual([]);
  });
});
