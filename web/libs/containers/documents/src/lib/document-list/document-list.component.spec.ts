import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import {
  DocumentListItem,
  DocumentStatus,
  DocumentType,
  PagedResultOfDocumentListItem
} from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { DocumentQuery, DocumentsStore } from '@ims/features/documents';
import { WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION, SEARCH_DEBOUNCE_MS } from '@ims/models';
import { DocumentListPageComponent } from './document-list.component';

const USER_ID = '00000000-0000-4000-8000-000000000004';

const ITEM: DocumentListItem = {
  id: '00000000-0000-4000-8000-000000000001',
  type: DocumentType.Issue,
  no: 'ISS-1',
  status: DocumentStatus.Draft,
  warehouseName: 'Main warehouse',
  partnerName: undefined,
  createdAt: '2026-10-09T01:00:00Z',
  postedAt: undefined,
  totalQty: 5,
  totalAmount: 0,
  createdBy: 'staff',
  createdById: '00000000-0000-4000-8000-000000000003',
  createdByName: 'Staff',
  submittedById: undefined,
  submittedBy: undefined,
  submittedByName: undefined,
  submittedAt: undefined,
  requiresApproval: false,
  requiresOverride: true,
  isLarge: true,
  isReturned: true,
  concurrencyToken: 'AAAA',
  canAct: false,
  amountIsEstimate: false
};

describe('DocumentListPageComponent Mine / All', () => {
  let spectator: Spectator<DocumentListPageComponent>;
  const mineFirst = signal(true);
  const granted = new Set<string>();
  const load = jest.fn<void, [DocumentQuery]>();
  const createComponent = createComponentFactory({
    component: DocumentListPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: PermissionService,
        useValue: {
          has: (permission: string) => granted.has(permission),
          $profile: () => ({ mineFirst: mineFirst() }),
          $userId: signal(USER_ID)
        }
      },
      {
        provide: DocumentsStore,
        useValue: {
          list: {
            $data: signal({
              items: [ITEM],
              page: 1,
              pageSize: 25,
              total: 1
            } as PagedResultOfDocumentListItem),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reload: jest.fn()
          }
        }
      },
      {
        provide: WarehousesStore,
        useValue: { $activeWarehouses: signal([]), ensureLoaded: jest.fn() }
      }
    ]
  });

  async function render(): Promise<void> {
    spectator = createComponent({ props: { type: DocumentType.Issue } as never });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const lastQuery = (): DocumentQuery => load.mock.calls[load.mock.calls.length - 1][0];

  beforeEach(() => {
    localStorage.clear();
    load.mockClear();
    granted.clear();
    granted.add(PERMISSION.DocumentCreate);
  });

  describe('the Amount column (cost.view, docs/design/mvp-ux.md 1.4)', () => {
    const headers = (): string[] =>
      spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
    const firstRow = (): string =>
      (spectator.query('tbody tr')?.textContent ?? '').replace(/\s+/g, ' ');

    it('warehouse staff get no Amount column and no number: the API sends null', async () => {
      mineFirst.set(true);
      await render();

      expect(headers()).not.toContain('Amount (RM)');
      expect(headers()).toContain('Status');
      expect(firstRow()).not.toMatch(/\d\.\d{2}/);
      expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers().length);
    });

    it('a clerk (cost.view) sees the Amount column', async () => {
      granted.add(PERMISSION.CostView);
      mineFirst.set(true);
      await render();

      expect(headers()).toContain('Amount (RM)');
    });

    it('a supervisor sees the Amount column too', async () => {
      granted.add(PERMISSION.CostView).add(PERMISSION.ValuationView);
      mineFirst.set(false);
      await render();

      expect(headers()).toContain('Amount (RM)');
    });
  });

  it('sends no search under the API minimum (the API answers 400), then sends it', async () => {
    mineFirst.set(false);
    await render();
    const typeAndWait = async (text: string): Promise<void> => {
      spectator.component.$searchInput.set(text);
      await new Promise(resolve => setTimeout(resolve, SEARCH_DEBOUNCE_MS + 50));
      spectator.detectChanges();
      await spectator.fixture.whenStable();
    };

    await typeAndWait('ab');
    expect(lastQuery().search).toBeUndefined();
    expect(load.mock.calls.every(([query]) => query.search === undefined)).toBe(true);

    await typeAndWait(' abc ');
    expect(lastQuery().search).toBe('abc');
  });

  it('warehouse staff start on Mine: the list asks the server for mine=true', async () => {
    mineFirst.set(true);

    await render();

    expect(lastQuery().mine).toBe(true);
  });

  it('a supervisor starts on All', async () => {
    mineFirst.set(false);

    await render();

    expect(lastQuery().mine).toBeUndefined();
  });

  it('R23: From after To says so under To and sends no request; fixing the range loads again', async () => {
    mineFirst.set(false);
    await render();
    load.mockClear();

    spectator.component.fromControl.setValue('2026-10-12');
    spectator.component.toControl.setValue('2026-10-10');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    const error = spectator.query(
      '#document-list-filters app-form-field:last-child .ui-form-field-error'
    );
    expect(error?.textContent?.trim()).toBe('From must be on or before To.');
    expect(load).not.toHaveBeenCalled();
    expect(spectator.query('tbody tr')).toBeNull();
    // The empty area says the same, not that no document matches (no search ran), and still offers Clear filters.
    const empty = spectator.query('app-empty-state')?.textContent ?? '';
    expect(empty).toContain('From must be on or before To.');
    expect(empty).not.toContain('match');
    expect(empty).toContain('Clear filters');

    spectator.component.toControl.setValue('2026-10-12');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(spectator.query('#document-list-filters .ui-form-field-error')).toBeNull();
    expect(lastQuery().from).toBe('2026-10-12');
    expect(lastQuery().to).toBe('2026-10-12');
  });

  it('the From and To filters are date fields in the app format, not native date inputs', async () => {
    mineFirst.set(false);
    await render();

    spectator.component.fromControl.setValue('2026-10-09');
    spectator.detectChanges();

    expect(spectator.queryAll('#document-list-filters app-date-field')).toHaveLength(2);
    const texts = spectator.queryAll<HTMLInputElement>('#document-list-filters input[type="text"]');
    expect(texts.map(text => text.value)).toContain('09 Oct 2026');
  });

  it('switching to All reloads without the filter and remembers the choice for next time', async () => {
    mineFirst.set(true);
    await render();

    spectator.component.onScopeChange('all');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(lastQuery().mine).toBeUndefined();
    expect(localStorage.getItem(`ims.documents.scope.${USER_ID}`)).toBe('all');
  });

  it('shows Returned, Override and Large next to the status of a document', async () => {
    await render();

    const chips = spectator
      .queryAll('app-status-chip')
      .map(chip => (chip.textContent ?? '').trim());

    expect(chips).toEqual(['Draft', 'Returned', 'Override', 'Large']);
  });

  describe('on a phone (NV-08)', () => {
    const filters = (): HTMLElement => spectator.query('#document-list-filters') as HTMLElement;
    const toggle = (): HTMLButtonElement =>
      spectator.query('.document-list-page-filter-toggle button') as HTMLButtonElement;

    it('the four filters hide behind one Filters button, closed at first', async () => {
      await render();

      expect(toggle().textContent).toContain('Filters');
      expect(toggle().getAttribute('aria-expanded')).toBe('false');
      expect(filters().classList).not.toContain('--open');
    });

    it('the button opens and closes the filters and says so to assistive technology', async () => {
      await render();

      spectator.click(toggle());
      expect(toggle().getAttribute('aria-expanded')).toBe('true');
      expect(toggle().getAttribute('aria-controls')).toBe('document-list-filters');
      expect(filters().classList).toContain('--open');

      spectator.click(toggle());
      expect(filters().classList).not.toContain('--open');
    });

    it('the button shows how many filters are set, so a hidden filter is not forgotten', async () => {
      await render();

      spectator.component.statusControl.setValue(DocumentStatus.Draft);
      spectator.component.fromControl.setValue('2026-10-01');
      spectator.detectChanges();

      expect(toggle().textContent).toContain('Filters (2)');
    });

    it('the card is two lines: number and chips, then the facts, with the warehouse on it', async () => {
      await render();

      expect(spectator.query('tr.ui-table-row')?.classList).toContain('--compact');
      expect(spectator.query('td.--phone-end app-status-chip')).toBeTruthy();
      expect(spectator.element.textContent).toContain('Main warehouse');
    });
  });
});
