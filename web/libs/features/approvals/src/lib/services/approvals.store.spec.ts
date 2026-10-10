import { signal } from '@angular/core';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import {
  DocumentCounts,
  DocumentListItem,
  DocumentStatus,
  DocumentType,
  DocumentsClient,
  PagedResultOfDocumentListItem
} from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { SystemSettingsStore } from '@ims/core/settings';
import { DocumentsStore } from '@ims/features/documents';
import { MAX_PAGE_SIZE, ROLE } from '@ims/models';
import { ApprovalsStore } from './approvals.store';

function item(id: number, overrides: Partial<DocumentListItem> = {}): DocumentListItem {
  return {
    id,
    type: DocumentType.Issue,
    no: `ISS-${id}`,
    status: DocumentStatus.Submitted,
    warehouseName: 'Main',
    partnerName: undefined,
    createdAt: '2026-10-09T01:00:00Z',
    postedAt: undefined,
    totalQty: 5,
    totalAmount: 0,
    createdBy: 'staff',
    createdById: 3,
    createdByName: 'Staff',
    submittedById: 3,
    submittedBy: 'staff',
    submittedByName: 'Staff',
    submittedAt: '2026-10-09T02:00:00Z',
    requiresApproval: true,
    requiresOverride: false,
    isLarge: false,
    isReturned: false,
    concurrencyToken: 'AAAA',
    canAct: true,
    amountIsEstimate: false,
    ...overrides
  };
}

function page(items: DocumentListItem[]): PagedResultOfDocumentListItem {
  return {
    items,
    page: 1,
    pageSize: MAX_PAGE_SIZE,
    total: items.length
  } as PagedResultOfDocumentListItem;
}

describe('ApprovalsStore', () => {
  let spectator: SpectatorService<ApprovalsStore>;
  const counts = signal<DocumentCounts | undefined>({
    returnedToMe: 0,
    myDrafts: 0,
    postedToday: 0,
    approvalsWaiting: 2
  });
  const createService = createServiceFactory({
    service: ApprovalsStore,
    providers: [
      mockProvider(DocumentsClient),
      mockProvider(DocumentsStore, { counts: { $data: counts, load: jest.fn() } }),
      mockProvider(PermissionService, {
        $userId: signal(9),
        $role: signal<string | undefined>(ROLE.Supervisor)
      }),
      mockProvider(SystemSettingsStore, {
        $allowSelfApproval: signal(false),
        ensureLoaded: jest.fn()
      })
    ]
  });

  beforeEach(() => {
    spectator = createService();
  });

  it('loads the inbox with ONE list call filtered on requiresApproval, never one call per document', () => {
    const client = spectator.inject(DocumentsClient);
    client.list.mockReturnValue(of(page([item(2), item(1)])));

    spectator.service.refresh();

    expect(client.list).toHaveBeenCalledTimes(1);
    const args = client.list.mock.calls[0];
    expect(args[1]).toBe(DocumentStatus.Submitted);
    expect(args[11]).toBe(true);
    expect(client.get).not.toHaveBeenCalled();
    expect(spectator.inject(DocumentsStore).counts.load).toHaveBeenCalled();
  });

  it('shows the oldest submission first and splits the tabs by who submitted', () => {
    const client = spectator.inject(DocumentsClient);
    client.list.mockReturnValue(
      of(
        page([
          item(3, { submittedAt: '2026-10-09T05:00:00Z' }),
          item(2, { submittedAt: '2026-10-09T03:00:00Z', submittedById: 9, canAct: false }),
          item(1, { submittedAt: '2026-10-09T01:00:00Z' }),
          item(4, { submittedAt: '2026-10-09T04:00:00Z', isLarge: true, canAct: false })
        ])
      )
    );

    spectator.service.refresh();

    expect(spectator.service.submitted.$data()?.map(d => d.id)).toEqual([1, 2, 4, 3]);
    expect(spectator.service.$waitingForMe().map(d => d.id)).toEqual([1, 3]);
    expect(spectator.service.$submittedByMe().map(d => d.id)).toEqual([2]);
  });

  it("trusts canAct from the API over the role rules (an Owner-level document is not the Supervisor's)", () => {
    const client = spectator.inject(DocumentsClient);
    client.list.mockReturnValue(
      of(page([item(1, { canAct: false }), item(2, { canAct: true, isLarge: true })]))
    );

    spectator.service.refresh();

    expect(spectator.service.$waitingForMe().map(d => d.id)).toEqual([2]);
  });

  it('takes the badge number from the server counts', () => {
    expect(spectator.service.$waitingCount()).toBe(2);
  });
});
