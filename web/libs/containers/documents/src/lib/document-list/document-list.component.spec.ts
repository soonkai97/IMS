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
import { PERMISSION, ROLE } from '@ims/models';
import { DocumentListPageComponent } from './document-list.component';

const ITEM: DocumentListItem = {
  id: 1,
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
  createdById: 3,
  createdByName: 'Staff',
  submittedById: undefined,
  submittedBy: undefined,
  submittedByName: undefined,
  submittedAt: undefined,
  requiresApproval: false,
  requiresOverride: true,
  isLarge: true,
  isReturned: true,
  concurrencyToken: 'AAAA'
};

describe('DocumentListPageComponent Mine / All', () => {
  let spectator: Spectator<DocumentListPageComponent>;
  const role = signal<string | undefined>(ROLE.WarehouseStaff);
  const load = jest.fn<void, [DocumentQuery]>();
  const createComponent = createComponentFactory({
    component: DocumentListPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: PermissionService,
        useValue: {
          has: (permission: string) => permission === PERMISSION.DocumentCreate,
          $role: role,
          $userId: signal(4)
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
  });

  it('warehouse staff start on Mine: the list asks the server for mine=true', async () => {
    role.set(ROLE.WarehouseStaff);

    await render();

    expect(lastQuery().mine).toBe(true);
  });

  it('a supervisor starts on All', async () => {
    role.set(ROLE.Supervisor);

    await render();

    expect(lastQuery().mine).toBeUndefined();
  });

  it('switching to All reloads without the filter and remembers the choice for next time', async () => {
    role.set(ROLE.WarehouseStaff);
    await render();

    spectator.component.onScopeChange('all');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(lastQuery().mine).toBeUndefined();
    expect(localStorage.getItem('ims.documents.scope.4')).toBe('all');
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
