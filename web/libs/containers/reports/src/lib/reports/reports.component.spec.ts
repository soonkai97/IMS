import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentStatus, DocumentType, MovementRow, PagedResultOfMovementRow } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { ReportsStore } from '@ims/features/reports';
import { WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiOperation,
  IDLE_OPERATION,
  PERMISSION,
  UNKNOWN_ERROR,
  failureOperation
} from '@ims/models';
import { ReportsPageComponent } from './reports.component';

const RANGE_SENTENCE = 'From must be on or before To.';
const SERVER_SENTENCE = "The 'from' date is after the 'to' date.";

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
  const operation = signal<ApiOperation>(IDLE_OPERATION);
  const failed = signal(false);
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
            $isFailed: failed,
            $operation: operation,
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
    operation.set(IDLE_OPERATION);
    failed.set(false);
    load.mockClear();
    spectator = createComponent();
    spectator.detectChanges();
  }

  function setRange(from: string, to: string): void {
    spectator.component.fromControl.setValue(from);
    spectator.component.toControl.setValue(to);
    spectator.detectChanges();
  }
  const toFieldError = (): string =>
    (
      spectator.query('.reports-page-filters app-form-field:last-child .ui-form-field-error')
        ?.textContent ?? ''
    ).trim();

  const SUPERVISOR = [PERMISSION.ReportsRead, PERMISSION.CostView, PERMISSION.ValuationView];

  it('shows the signed Stock effect next to the unsigned Quantity: +24 for a receipt, -24 for an issue', () => {
    render(
      [
        movement({}, true),
        movement({ docNo: 'ISS-1', type: DocumentType.Issue, stockEffect: -24 }, true)
      ],
      ...SUPERVISOR
    );

    expect(headers()).toEqual(expect.arrayContaining(['Quantity', 'Stock effect', 'Amount (RM)']));
    expect(effects()).toEqual(['+24', '-24']);
  });

  it('shows Product, Unit and Unit cost (a unit price, up to 4 decimals) next to the SKU and quantity (PR-06)', () => {
    render([movement({ unitCost: 3.3333 }, true)], ...SUPERVISOR);

    expect(headers()).toEqual([
      'No.',
      'Posted',
      'Type',
      'Status',
      'Warehouse',
      'Partner',
      'SKU',
      'Product',
      'Quantity',
      'Unit',
      'Stock effect',
      'Unit cost (RM per base unit)',
      'Amount (RM)'
    ]);
    const cells = spectator
      .queryAll('tbody tr:first-child td')
      .map(cell => (cell.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(cells.slice(6)).toEqual([
      'DEMO-001',
      'Demo widget',
      '2',
      'CTN',
      '+24',
      '3.3333',
      '36.00'
    ]);
  });

  it('keeps a SKU and a date on one line (PR-06)', () => {
    render([movement({}, true)], ...SUPERVISOR);

    const nowrap = spectator.queryAll('.reports-page-nowrap').map(cell => cell.textContent?.trim());
    expect(nowrap).toEqual(['09/10/2026 09:00', 'DEMO-001']);
  });

  it('a bad range shows the sentence under To, sends no request and shows no old rows (PR-03)', () => {
    render([movement({}, true)], ...SUPERVISOR);
    expect(load).toHaveBeenCalledTimes(1);
    expect(spectator.queryAll('tbody tr')).toHaveLength(1);

    setRange('2026-10-12', '2026-10-10');

    expect(toFieldError()).toBe(RANGE_SENTENCE);
    expect(load).toHaveBeenCalledTimes(1);
    expect(spectator.queryAll('tbody tr')).toHaveLength(0);
    expect(spectator.query('app-pager')).toBeNull();
    expect(spectator.query('.ui-table-state button')).toBeNull();
    expect(spectator.query('.ui-table-scroll')).toBeNull();
    expect(spectator.element.textContent).not.toContain('No data for these filters.');

    // The sentence is tied to the To input, which is marked invalid.
    const toInput = spectator.query('.reports-page-filters app-form-field:last-child input');
    const errorId = spectator.query('.ui-form-field-error')?.id ?? '';
    expect(errorId).not.toBe('');
    expect(toInput?.getAttribute('aria-describedby')).toContain(errorId);
    expect(toInput?.getAttribute('aria-invalid')).toBe('true');

    // The export stays focusable and says why it does nothing.
    const exportButton = spectator.query('.reports-page-toolbar button');
    expect(exportButton?.getAttribute('aria-disabled')).toBe('true');
    const hintId = exportButton?.getAttribute('aria-describedby') ?? '';
    expect(spectator.query(`#${hintId}`)?.textContent).toContain(RANGE_SENTENCE);
  });

  it('correcting the range removes the sentence and loads again', () => {
    render([movement({}, true)], ...SUPERVISOR);
    setRange('2026-10-12', '2026-10-10');

    setRange('2026-10-09', '2026-10-10');

    expect(toFieldError()).toBe('');
    expect(load).toHaveBeenCalledTimes(2);
    expect(load).toHaveBeenLastCalledWith(
      expect.objectContaining({ from: '2026-10-09', to: '2026-10-10' })
    );
    expect(spectator.queryAll('tbody tr')).toHaveLength(1);
  });

  it('a 400 under errors.to shows the server sentence at To, with no Retry and no old footer', () => {
    render([movement({}, true)], ...SUPERVISOR);

    failed.set(true);
    operation.set(
      failureOperation({
        isApiError: true,
        kind: API_ERROR_KIND.Validation,
        status: 400,
        title: 'Validation',
        detail: SERVER_SENTENCE,
        fieldErrors: { to: [SERVER_SENTENCE] },
        extensions: {}
      })
    );
    spectator.detectChanges();

    expect(toFieldError()).toBe(SERVER_SENTENCE);
    expect(spectator.query('.ui-table-state button')).toBeNull();
    expect(spectator.query('app-pager')).toBeNull();
    expect(spectator.queryAll('tbody tr')).toHaveLength(0);
    expect(spectator.element.textContent).not.toContain('No data for these filters.');
  });

  it('another failure keeps "Something went wrong" with Retry and hides the old footer', () => {
    render([movement({}, true)], ...SUPERVISOR);

    failed.set(true);
    operation.set(failureOperation({ ...UNKNOWN_ERROR, status: 500 }));
    spectator.detectChanges();

    expect(toFieldError()).toBe('');
    expect(spectator.query('.ui-table-state button')).not.toBeNull();
    expect(spectator.query('app-pager')).toBeNull();
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

    expect(headers()).not.toContain('Amount (RM)');
    expect(headers()).not.toContain('Unit cost (RM per base unit)');
    expect(headers()).toEqual(expect.arrayContaining(['Product', 'Unit', 'Stock effect']));
    expect(bodyRows()[0]).not.toMatch(/\d\.\d{2}/);
    expect(bodyRows()[0]).toContain('Demo widget');
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
