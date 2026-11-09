import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PagedResultOfPriceListListItem, PriceListListItem } from '@ims/api';
import { PriceListQuery, PriceListsStore } from '@ims/features/sales';
import { PriceListListPageComponent } from './price-list-list.component';

const DEFAULT: PriceListListItem = {
  id: '00000000-0000-4000-8000-000000000001',
  name: 'Retail',
  isDefault: true,
  customerId: undefined,
  customerName: undefined,
  itemCount: 12,
  concurrencyToken: 'AAAA'
};
const FOR_CUSTOMER: PriceListListItem = {
  ...DEFAULT,
  id: '00000000-0000-4000-8000-000000000002',
  name: 'Acme prices',
  isDefault: false,
  customerId: 'c1',
  customerName: 'Acme Trading',
  itemCount: 1
};

describe('PriceListListPageComponent', () => {
  let spectator: Spectator<PriceListListPageComponent>;
  const load = jest.fn<void, [PriceListQuery]>();
  const data = signal<PagedResultOfPriceListListItem | undefined>(undefined);
  const createComponent = createComponentFactory({
    component: PriceListListPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: PriceListsStore,
        useValue: {
          list: {
            $data: data,
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reset: jest.fn(),
            reload: jest.fn()
          }
        }
      }
    ]
  });

  async function render(items: PriceListListItem[]): Promise<void> {
    data.set({ items, page: 1, pageSize: 25, total: items.length });
    spectator = createComponent();
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }
  const rows = (): string[] =>
    spectator.queryAll('tbody tr').map(row => (row.textContent ?? '').replace(/\s+/g, ' '));

  beforeEach(() => load.mockClear());

  it('shows the name, the customer, how many prices, and marks the default list; the default list is for every other customer', async () => {
    await render([DEFAULT, FOR_CUSTOMER]);

    expect(rows()[0]).toContain('Retail');
    expect(rows()[0]).toContain('Default');
    expect(rows()[0]).toContain('Every customer without a list of their own');
    expect(rows()[0]).toContain('12 prices');
    expect(rows()[1]).toContain('Acme Trading');
    expect(rows()[1]).toContain('1 price');
    expect(rows()[1]).not.toContain('Default');
  });

  it('shows no price: the list carries none, and the headers say so', async () => {
    await render([DEFAULT]);

    expect(spectator.element.textContent).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('th').map(th => (th.textContent ?? '').trim())).not.toContain(
      'Price (RM)'
    );
  });

  it('opens the list from a row and offers New price list', async () => {
    await render([DEFAULT]);

    expect(spectator.query('tbody a')?.getAttribute('href')).toBe(`/price-lists/${DEFAULT.id}`);
    expect(spectator.query('a[href="/price-lists/new"]')).not.toBeNull();
  });

  it('asks the API for the first page and ignores a search under the minimum', async () => {
    await render([]);

    expect(load.mock.calls[0][0]).toMatchObject({ search: undefined, page: 1 });
    expect(spectator.element.textContent).toContain('No price lists yet.');
  });
});
