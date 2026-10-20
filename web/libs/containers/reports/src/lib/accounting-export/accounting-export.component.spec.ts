import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { NEVER, of } from 'rxjs';
import { WarehouseDto } from '@ims/api';
import { provideUiText } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { AccountingExportStore } from '@ims/features/reports';
import { WarehousesStore } from '@ims/features/warehousing';
import { ApiError, PERMISSION } from '@ims/models';
import { AccountingExportPageComponent } from './accounting-export.component';

const MAIN = '00000000-0000-4000-8000-0000000000a1';
const CAP_SENTENCE =
  'Too many rows: one accounting file holds at most 50000 rows. Narrow the date range (for example one month at a time) or pick one warehouse.';

const warehouse = (id: string, name: string): WarehouseDto => ({
  id,
  name,
  code: name.slice(0, 4).toUpperCase(),
  contact: undefined,
  notes: undefined,
  isActive: true
});

describe('AccountingExportPageComponent (Reports > Accounting export)', () => {
  let spectator: Spectator<AccountingExportPageComponent>;
  const run = jest.fn();
  const reset = jest.fn();
  const running = signal(false);
  const operation = signal<{ error?: Partial<ApiError> }>({});
  const lastFileName = signal<string | undefined>(undefined);
  const held = new Set<string>();

  const createComponent = createComponentFactory({
    component: AccountingExportPageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      provideRouter([]),
      { provide: PermissionService, useValue: { has: (code: string) => held.has(code) } },
      {
        provide: WarehousesStore,
        useValue: {
          ensureLoaded: jest.fn(),
          $activeWarehouses: signal([warehouse(MAIN, 'Main warehouse')])
        }
      },
      {
        provide: AccountingExportStore,
        useValue: {
          $lastFileName: lastFileName,
          download: { $isRunning: running, $operation: operation, run, reset }
        }
      }
    ]
  });

  const text = () => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';
  const download = () =>
    spectator.queryAll<HTMLButtonElement>('button').find(b => b.textContent?.includes('Download'));
  const dateInput = (name: string) =>
    spectator.query<HTMLInputElement>(`app-date-field[formControlName="${name}"] input[type=text]`);

  async function settle(): Promise<void> {
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  beforeEach(async () => {
    jest.clearAllMocks();
    held.clear();
    held.add(PERMISSION.AccountingExport);
    running.set(false);
    operation.set({});
    lastFileName.set(undefined);
    run.mockReturnValue(of({ data: new Blob(), fileName: 'ims-accounting.xlsx' }));
    spectator = createComponent();
    await settle();
  });

  it('says what the file is: a fixed layout, at cost, reversals as negative rows, and claims nothing about other software', () => {
    expect(text()).toContain('The layout is fixed');
    expect(text()).toContain('Amounts are at cost.');
    expect(text()).toContain('Reversals appear as negative rows');
    expect(text()).not.toMatch(/SQL Account|AutoCount|sample/i);
  });

  it('asks for a period and offers the warehouses the person may read, with "all" as the default', () => {
    expect(text()).toContain('From');
    expect(text()).toContain('To');
    expect(text()).toContain('All my warehouses');
    expect(spectator.component.$warehouseOptions().map(option => option.label)).toEqual([
      'Main warehouse'
    ]);
  });

  it('refuses an empty period before anything is sent, says which date is missing and puts the focus on the first', async () => {
    spectator.click(download() as HTMLButtonElement);
    await settle();

    expect(run).not.toHaveBeenCalled();
    expect(text()).toContain('Enter the first date of the period.');
    expect(text()).toContain('Enter the last date of the period.');
    expect(document.activeElement).toBe(dateInput('from'));
  });

  it('refuses a first date after the last date and says so under To', async () => {
    spectator.component.form.patchValue({ from: '2026-10-31', to: '2026-10-01' });
    spectator.click(download() as HTMLButtonElement);
    await settle();

    expect(run).not.toHaveBeenCalled();
    expect(text()).toContain('From must be on or before To.');
  });

  it('downloads the period (yyyy-MM-dd) for all warehouses, then says which file it handed over', async () => {
    spectator.component.form.patchValue({ from: '2026-10-01', to: '2026-10-31' });
    spectator.click(download() as HTMLButtonElement);
    expect(run).toHaveBeenCalledWith({
      from: '2026-10-01',
      to: '2026-10-31',
      warehouseId: undefined
    });

    lastFileName.set('ims-accounting-2026-10-01-2026-10-31.xlsx');
    await settle();
    expect(text()).toContain('Downloaded ims-accounting-2026-10-01-2026-10-31.xlsx.');
  });

  it('sends the warehouse that was picked', () => {
    spectator.component.form.patchValue({
      from: '2026-10-01',
      to: '2026-10-31',
      warehouseId: MAIN
    });

    spectator.click(download() as HTMLButtonElement);

    expect(run).toHaveBeenCalledWith({ from: '2026-10-01', to: '2026-10-31', warehouseId: MAIN });
  });

  it('shows the sentence of the row cap in one alert, with the narrower-range advice', async () => {
    operation.set({ error: { status: 400, fieldErrors: {}, message: CAP_SENTENCE } });
    await settle();

    const alerts = spectator.queryAll('[role="alert"]');
    expect(alerts).toHaveLength(1);
    expect(alerts[0].textContent).toContain('Narrow the date range');
  });

  it('shows the sentences of a field-level 400 (a date outside the allowed years) and nothing else', async () => {
    operation.set({
      error: {
        status: 400,
        fieldErrors: { to: ['Enter dates between 1 January 2000 and 31 December 2100.'] },
        message: 'Validation failed.'
      }
    });
    await settle();

    expect(spectator.queryAll('[role="alert"]')).toHaveLength(1);
    expect(text()).toContain('Enter dates between 1 January 2000 and 31 December 2100.');
    expect(text()).not.toContain('Validation failed.');
  });

  it('a double click while the file is being made sends one request: the button is busy and inert', async () => {
    run.mockReturnValue(NEVER);
    spectator.component.form.patchValue({ from: '2026-10-01', to: '2026-10-31' });
    spectator.click(download() as HTMLButtonElement);
    running.set(true);
    await settle();

    expect(download()?.getAttribute('aria-busy')).toBe('true');
    expect(download()?.getAttribute('aria-disabled')).toBe('true');
    spectator.click(download() as HTMLButtonElement);

    expect(run).toHaveBeenCalledTimes(1);
  });

  it('forgets the last answer when the page opens again', () => {
    expect(reset).toHaveBeenCalledTimes(1);
    expect(lastFileName()).toBeUndefined();
  });

  it('sends nothing for a person without the permission (the route refuses them first)', () => {
    held.clear();
    spectator.component.form.patchValue({ from: '2026-10-01', to: '2026-10-31' });

    spectator.click(download() as HTMLButtonElement);

    expect(run).not.toHaveBeenCalled();
  });
});
