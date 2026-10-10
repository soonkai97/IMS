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
});
