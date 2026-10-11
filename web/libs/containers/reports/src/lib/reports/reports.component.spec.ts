import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentStatus, DocumentType, MovementRow, PagedResultOfMovementRow } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { ReportsStore } from '@ims/features/reports';
import { WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION } from '@ims/models';
import { ReportsPageComponent } from './reports.component';

/** A movement row as the API sends it: unitCost and amount are null without cost.view. */
function movement(overrides: Partial<MovementRow>, withCost: boolean): MovementRow {
  const hidden = null as unknown as undefined;
  return {
    postedAt: '2026-10-09T01:00:00Z',
    docNo: 'RCP-1',
    type: DocumentType.Receipt,
    status: DocumentStatus.Posted,
    warehouseCode: 'WH01',
    partner: undefined,
    sku: 'DEMO-001',
    productName: 'Demo widget',
    qty: 2,
    uomCode: 'CTN',
    baseQty: 24,
    stockEffect: 24,
    unitCost: withCost ? 1.5 : hidden,
    amount: withCost ? 36 : hidden,
    isReversal: false,
    ...overrides
  };
}

describe('ReportsPageComponent receipt and issue detail (3.9 signed stock effect, 1.4 cost)', () => {
  let spectator: Spectator<ReportsPageComponent>;
  const granted = new Set<string>();
  const load = jest.fn();
  const rows = signal<MovementRow[]>([]);
  const createComponent = createComponentFactory({
    component: ReportsPageComponent,
    detectChanges: false,
    providers: [
      {
        provide: PermissionService,
        useValue: { has: (permission: string) => granted.has(permission) }
      },
      {
        provide: ReportsStore,
        useFactory: () => ({
          movements: {
            $data: signal({
              items: rows(),
              page: 1,
              pageSize: 25,
              total: rows().length
            } as PagedResultOfMovementRow),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reload: jest.fn()
          },
          exportMovements: { $isRunning: signal(false) },
          exportStockSummary: { $isRunning: signal(false) },
          exportLedger: { $isRunning: signal(false) },
          exportLowStock: { $isRunning: signal(false) }
        })
      },
      {
        provide: WarehousesStore,
        useValue: { ensureLoaded: jest.fn(), $activeWarehouses: signal([]) }
      }
    ]
  });

  const headers = (): string[] =>
    spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
  const bodyRows = (): string[] =>
    spectator.queryAll('tbody tr').map(tr => (tr.textContent ?? '').replace(/\s+/g, ' ').trim());
  const effects = (): string[] =>
    spectator.queryAll('.reports-page-effect').map(cell => (cell.textContent ?? '').trim());

  function render(data: MovementRow[], ...permissions: string[]): void {
    granted.clear();
    permissions.forEach(permission => granted.add(permission));
    rows.set(data);
    spectator = createComponent();
    spectator.detectChanges();
  }

  const SUPERVISOR = [PERMISSION.ReportsRead, PERMISSION.CostView, PERMISSION.ValuationView];

  it('shows the signed Stock effect next to the unsigned Quantity: +24 for a receipt, -24 for an issue', () => {
    render(
      [
        movement({}, true),
        movement({ docNo: 'ISS-1', type: DocumentType.Issue, stockEffect: -24 }, true)
      ],
      ...SUPERVISOR
    );

    expect(headers()).toEqual(expect.arrayContaining(['Quantity', 'Stock effect', 'Amount']));
    expect(effects()).toEqual(['+24', '-24']);
    expect(bodyRows()[0]).toContain('2 CTN');
  });

  it('the sign is text, and the colour classes only reinforce it', () => {
    render(
      [movement({}, true), movement({ docNo: 'ISS-1', stockEffect: -24 }, true)],
      ...SUPERVISOR
    );

    const [plus, minus] = spectator.queryAll('.reports-page-effect');
    expect(plus.classList).toContain('--in');
    expect(plus.textContent).toContain('+');
    expect(minus.classList).toContain('--out');
    expect(minus.textContent).toContain('-');
  });

  it('a reversal row carries a Reversal badge with text, the posting it undoes does not', () => {
    render(
      [
        movement(
          { docNo: 'RCP-9', status: DocumentStatus.Reversed, stockEffect: -24, isReversal: true },
          true
        ),
        movement({ docNo: 'RCP-9', status: DocumentStatus.Reversed }, true),
        movement({ docNo: 'RCP-1' }, true)
      ],
      ...SUPERVISOR
    );

    const [reversal, posting, plain] = bodyRows();
    expect(reversal).toContain('RCP-9');
    expect(reversal).toContain('Reversal');
    expect(reversal).toContain('-24');
    expect(posting).not.toContain('Reversal');
    expect(posting).toContain('+24');
    expect(plain).not.toContain('Reversal');
    expect(spectator.queryAll('app-status-chip')).toHaveLength(1);
  });

  it('without cost.view there is no Amount column and no number (a role that reaches the report)', () => {
    render([movement({}, false)], PERMISSION.ReportsRead);

    expect(headers()).not.toContain('Amount');
    expect(headers()).toContain('Stock effect');
    expect(bodyRows()[0]).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers().length);
  });

  it('with cost.view the Amount column shows the amount', () => {
    render([movement({}, true)], ...SUPERVISOR);

    expect(bodyRows()[0]).toContain('36.00');
  });

  it('has the Excel export for a role that reaches the report', () => {
    render([movement({}, true)], ...SUPERVISOR);

    expect(spectator.query('.reports-page-toolbar button')?.textContent).toContain('Export Excel');
  });
});
