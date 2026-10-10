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

describe('ProductPickerComponent keyboard and scanner path (NV-03, NV-04, NV-15)', () => {
  let spectator: Spectator<ProductPickerComponent>;
  const createComponent = createComponentFactory({
    component: ProductPickerComponent,
    providers: [mockProvider(ProductsStore), mockProvider(StockAvailabilityService)]
  });

  beforeEach(() => {
    spectator = createComponent();
  });

  it('marks the search box for focus-on-open, so the sheet that holds it focuses it when it opens', () => {
    expect(spectator.query('input[type=search]')?.hasAttribute('data-autofocus')).toBe(true);
  });

  it('says what to type until there are two characters, and tells the user about a pick instead', () => {
    expect(spectator.query('.product-picker-hint')?.textContent).toContain(
      'Type 2 or more letters'
    );

    spectator.setInput({ notice: 'W-1: now 2.' } as never);
    spectator.detectChanges();

    expect(spectator.query('.product-picker-notice')?.textContent).toContain('W-1: now 2.');
    expect(spectator.query('.product-picker-hint')).toBeNull();
  });

  it('the notice sits in a live region that exists before the message, so it is read out', () => {
    expect(spectator.query('[aria-live=polite]')).toBeTruthy();
  });

  it('tells the page when the user types something else, but not when the box is cleared after a pick', () => {
    const terms: string[] = [];
    spectator.component.$termChanged.subscribe(term => terms.push(term));

    spectator.component.$term.set('bo');
    spectator.detectChanges();
    spectator.component.$term.set('');
    spectator.detectChanges();

    expect(terms).toEqual(['bo']);
  });

  it('tapping a result keeps the cursor in the search box for the next scan', () => {
    const input = spectator.query('input[type=search]') as HTMLInputElement;
    expect(document.activeElement).not.toBe(input);

    spectator.component.onPick({ product: WIDGET, available: null });

    expect(document.activeElement).toBe(input);
  });
});
