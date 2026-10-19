import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentListItem, DocumentStatus, DocumentType } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { ApprovalsStore } from '@ims/features/approvals';
import { PERMISSION } from '@ims/models';
import { ApprovalsInboxPageComponent } from './inbox.component';

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

function item(id: number, overrides: Partial<DocumentListItem> = {}): DocumentListItem {
  return {
    id: guid(id),
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
    createdById: guid(3),
    createdByName: 'Staff',
    submittedById: guid(3),
    submittedBy: 'staff',
    submittedByName: 'Staff Member',
    submittedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
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

describe('ApprovalsInboxPageComponent', () => {
  let spectator: Spectator<ApprovalsInboxPageComponent>;
  const refresh = jest.fn();
  const waiting = signal<DocumentListItem[]>([]);
  const mine = signal<DocumentListItem[]>([]);
  const granted = new Set<string>();
  const createComponent = createComponentFactory({
    component: ApprovalsInboxPageComponent,
    providers: [
      provideRouter([]),
      {
        provide: PermissionService,
        useValue: { has: (permission: string) => granted.has(permission) }
      },
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
    granted.clear();
    granted.add(PERMISSION.CostView);
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
    expect(first).toContain('Stock override');
    expect(first).toContain('5 min');
    expect(second).toContain('Other Staff');
    expect(second).toContain('Large');
  });

  it('PS-03: shows the value that decides approval, marked as an estimate for an unposted issue', () => {
    waiting.set([item(1, { totalAmount: 11250.5, amountIsEstimate: true })]);
    spectator = createComponent();

    expect(rows()[0]).toContain('11,250.50');
    expect(rows()[0]).toContain('est.');
  });

  it('PS-03: a value that is not an estimate carries no mark', () => {
    waiting.set([item(1, { totalAmount: 800, amountIsEstimate: false })]);
    spectator = createComponent();

    expect(rows()[0]).toContain('800.00');
    expect(rows()[0]).not.toContain('est.');
  });

  it('without cost.view there is no Amount column and no number, but the flags still say why', () => {
    granted.clear();
    waiting.set([item(1, { totalAmount: undefined, isLarge: true, requiresOverride: true })]);
    spectator = createComponent();

    const headers = spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
    expect(headers).not.toContain('Amount (RM)');
    expect(rows()[0]).toContain('Large');
    expect(rows()[0]).toContain('Stock override');
    expect(rows()[0]).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers.length);
  });

  it('with cost.view the Amount column is there', () => {
    const headers = spectator.queryAll('th').map(header => (header.textContent ?? '').trim());

    expect(headers).toContain('Amount (RM)');
  });

  it('shows what I submitted on the other tab', () => {
    spectator.component.onTabChange('mine');
    spectator.detectChanges();

    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('ISS-3');
  });
});
