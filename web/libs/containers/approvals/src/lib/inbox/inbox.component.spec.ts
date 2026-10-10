import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentListItem, DocumentStatus, DocumentType } from '@ims/api';
import { ApprovalsStore } from '@ims/features/approvals';
import { ApprovalsInboxPageComponent } from './inbox.component';

function item(id: number, overrides: Partial<DocumentListItem> = {}): DocumentListItem {
  return {
    id,
    type: DocumentType.Issue,
    no: `ISS-${id}`,
    status: DocumentStatus.Submitted,
    warehouseName: 'Main warehouse',
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
    submittedByName: 'Staff Member',
    submittedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    requiresApproval: true,
    requiresOverride: false,
    isLarge: false,
    isReturned: false,
    concurrencyToken: 'AAAA',
    ...overrides
  };
}

describe('ApprovalsInboxPageComponent', () => {
  let spectator: Spectator<ApprovalsInboxPageComponent>;
  const refresh = jest.fn();
  const waiting = signal<DocumentListItem[]>([]);
  const mine = signal<DocumentListItem[]>([]);
  const createComponent = createComponentFactory({
    component: ApprovalsInboxPageComponent,
    providers: [
      provideRouter([]),
      {
        provide: ApprovalsStore,
        useValue: {
          submitted: { $isLoading: signal(false), $isFailed: signal(false) },
          $waitingForMe: waiting,
          $submittedByMe: mine,
          refresh
        }
      }
    ]
  });

  const rows = (): string[] =>
    spectator.queryAll('tbody tr').map(row => (row.textContent ?? '').replace(/\s+/g, ' ').trim());

  beforeEach(() => {
    refresh.mockClear();
    waiting.set([
      item(1, { requiresOverride: true }),
      item(2, { isLarge: true, submittedByName: 'Other Staff' })
    ]);
    mine.set([item(3)]);
    spectator = createComponent();
  });

  it('loads the inbox once on open', () => {
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('lists the documents waiting for me with who submitted, the flags and how long they wait', () => {
    const [first, second] = rows();

    expect(first).toContain('ISS-1');
    expect(first).toContain('Staff Member');
    expect(first).toContain('Override');
    expect(first).toContain('5 min');
    expect(second).toContain('Other Staff');
    expect(second).toContain('Large');
  });

  it('shows what I submitted on the other tab', () => {
    spectator.component.onTabChange('mine');
    spectator.detectChanges();

    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('ISS-3');
  });
});
