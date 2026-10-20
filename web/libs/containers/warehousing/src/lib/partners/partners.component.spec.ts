import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { throwError } from 'rxjs';
import { PagedResultOfPartnerDto, PartnerDto, PartnerType } from '@ims/api';
import { ModulesStore, PermissionService } from '@ims/core/identity';
import { PartnersStore } from '@ims/features/warehousing';
import { API_ERROR_KIND, ApiError, PERMISSION } from '@ims/models';
import { ApiCommand } from '@ims/utils';
import { PartnersPageComponent } from './partners.component';

const ROW: PartnerDto = {
  id: '00000000-0000-4000-8000-000000000001',
  type: PartnerType.Supplier,
  name: 'Acme Trading',
  contact: undefined,
  isActive: true
};

const ROW_WITH_TAX_IDS: PartnerDto = { ...ROW, tin: 'C1234567890', sstRegNo: 'W10-1808-32000001' };

const DUPLICATE: ApiError = {
  isApiError: true,
  kind: API_ERROR_KIND.Conflict,
  status: 409,
  title: 'Conflict',
  detail: "A supplier named 'Acme Trading' already exists.",
  fieldErrors: {},
  extensions: {}
};

describe('PartnersPageComponent (Clerk creates, Supervisor and Admin manage)', () => {
  let spectator: Spectator<PartnersPageComponent>;
  const granted = new Set<string>();
  const saveCalls: unknown[] = [];
  let saveFails = false;
  let accountingOn = true;
  const createComponent = createComponentFactory({
    component: PartnersPageComponent,
    detectChanges: false,
    providers: [
      { provide: ModulesStore, useValue: { isEnabled: () => accountingOn } },
      {
        provide: PermissionService,
        useValue: { has: (permission: string) => granted.has(permission) }
      },
      {
        provide: PartnersStore,
        useFactory: () => ({
          list: {
            $data: signal({
              items: [ROW],
              page: 1,
              pageSize: 25,
              total: 1
            } as PagedResultOfPartnerDto),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load: jest.fn(),
            reload: jest.fn()
          },
          save: new ApiCommand<unknown, PartnerDto>(args => {
            saveCalls.push(args);
            return saveFails ? throwError(() => DUPLICATE) : throwError(() => new Error('unused'));
          })
        })
      }
    ]
  });

  function render(...permissions: string[]): void {
    granted.clear();
    permissions.forEach(permission => granted.add(permission));
    spectator = createComponent();
    spectator.detectChanges();
  }

  const buttonLabels = (): string[] =>
    spectator.queryAll('main button, button').map(button => (button.textContent ?? '').trim());
  const headers = (): string[] =>
    spectator.queryAll('th').map(header => (header.textContent ?? '').trim());

  beforeEach(() => {
    saveCalls.length = 0;
    saveFails = true;
    accountingOn = true;
  });

  it('Clerk: sees New partner but no Edit and no Actions column', () => {
    render(PERMISSION.MasterDataRead, PERMISSION.PartnersCreate);

    expect(buttonLabels().some(label => label.includes('New partner'))).toBe(true);
    expect(buttonLabels().some(label => label.includes('Edit'))).toBe(false);
    expect(headers()).not.toContain('Actions');
  });

  it('Supervisor and Admin: New partner and Edit on every row', () => {
    render(PERMISSION.MasterDataRead, PERMISSION.PartnersCreate, PERMISSION.MasterDataManage);

    expect(buttonLabels().some(label => label.includes('New partner'))).toBe(true);
    expect(buttonLabels().some(label => label.includes('Edit'))).toBe(true);
    expect(headers()).toContain('Actions');
  });

  it('ER-10: the Edit button of a row is named after the partner, so a list of them is not eight times "Edit"', () => {
    render(PERMISSION.MasterDataRead, PERMISSION.PartnersCreate, PERMISSION.MasterDataManage);

    const edit = spectator.queryAll('button').find(button => button.textContent?.includes('Edit'));
    expect(edit?.getAttribute('aria-label')).toBe('Edit Acme Trading (Supplier)');
  });

  it('read-only roles see neither New partner nor Edit', () => {
    render(PERMISSION.MasterDataRead);

    expect(buttonLabels().some(label => label.includes('New partner'))).toBe(false);
    expect(buttonLabels().some(label => label.includes('Edit'))).toBe(false);
  });

  it('creating has no Active checkbox; the 409 duplicate sentence shows on the name field', () => {
    render(PERMISSION.MasterDataRead, PERMISSION.PartnersCreate);

    spectator.component.onAdd();
    spectator.detectChanges();
    expect(spectator.query('dialog input[type=checkbox]')).toBeNull();

    spectator.component.form.controls.name.setValue('Acme Trading');
    spectator.component.onSave();
    spectator.detectChanges();

    expect(saveCalls).toHaveLength(1);
    expect(spectator.component.form.controls.name.errors).toEqual({ server: DUPLICATE.detail });
    expect(spectator.element.textContent).toContain(DUPLICATE.detail);
  });

  it('editing offers the Active checkbox', () => {
    render(PERMISSION.MasterDataRead, PERMISSION.MasterDataManage);

    spectator.component.onEdit(ROW);
    spectator.detectChanges();

    expect(spectator.query('dialog input[type=checkbox]')).not.toBeNull();
  });

  describe('tax identifiers of the accounting module (PL1.8)', () => {
    const input = (name: string) =>
      spectator.query<HTMLInputElement>(`dialog app-input[formControlName="${name}"] input`);
    const text = () => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';
    const body = () => (saveCalls[0] as { id: string; body: Record<string, unknown> }).body;

    function edit(row: PartnerDto): void {
      render(PERMISSION.MasterDataRead, PERMISSION.MasterDataManage);
      spectator.component.onEdit(row);
      spectator.detectChanges();
    }

    it('shows both identifiers with the value they loaded while the module is on', () => {
      edit(ROW_WITH_TAX_IDS);

      expect(input('tin')?.value).toBe('C1234567890');
      expect(input('sstRegNo')?.value).toBe('W10-1808-32000001');
    });

    it('ROUND TRIP: editing only the name sends both identifiers exactly as they were loaded', () => {
      edit(ROW_WITH_TAX_IDS);

      spectator.component.form.controls.name.setValue('Acme Trading Sdn Bhd');
      spectator.component.onSave();

      expect(saveCalls).toHaveLength(1);
      expect((saveCalls[0] as { id: string }).id).toBe(ROW.id);
      expect(body()).toMatchObject({
        name: 'Acme Trading Sdn Bhd',
        tin: 'C1234567890',
        sstRegNo: 'W10-1808-32000001'
      });
    });

    it('sends both keys even for a partner without identifiers (a save replaces them, so a missing key would clear)', () => {
      edit(ROW);

      spectator.component.onSave();

      expect(body()['tin']).toBe('');
      expect(body()['sstRegNo']).toBe('');
    });

    it('an identifier that was emptied goes as empty text, which clears it; the other one is kept', () => {
      edit(ROW_WITH_TAX_IDS);

      spectator.component.form.controls.tin.setValue('  ');
      spectator.component.onSave();

      expect(body()['tin']).toBe('');
      expect(body()['sstRegNo']).toBe('W10-1808-32000001');
    });

    it('sends what the server would store: no spaces at the ends, the case as typed', () => {
      edit(ROW);

      spectator.component.form.controls.tin.setValue(' c123abc ');
      spectator.component.onSave();

      expect(body()['tin']).toBe('c123abc');
    });

    it('a new partner (a Clerk may create one) shows the fields and sends both, empty', () => {
      render(PERMISSION.MasterDataRead, PERMISSION.PartnersCreate);
      spectator.component.onAdd();
      spectator.detectChanges();
      spectator.component.form.controls.name.setValue('New Co');

      spectator.component.onSave();

      expect(input('tin')).not.toBeNull();
      expect(body()).toMatchObject({ name: 'New Co', tin: '', sstRegNo: '' });
    });

    it('a wrong identifier is refused before anything is sent, in words without repeating the value, and the focus goes to it', async () => {
      edit(ROW);

      spectator.component.form.controls.tin.setValue('C-1234');
      spectator.component.onSave();
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(saveCalls).toHaveLength(0);
      expect(text()).toContain('TIN may contain only letters and digits, up to 20 characters.');
      expect(text()).not.toContain('C-1234');
      expect(input('tin')?.getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(input('tin'));
    });

    it('an empty name is refused before anything is sent and the focus goes to the name', async () => {
      edit(ROW);

      spectator.component.form.controls.name.setValue('');
      spectator.component.onSave();
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(saveCalls).toHaveLength(0);
      expect(document.activeElement).toBe(input('name'));
    });

    it('the module off: nothing is shown and neither key is sent (the server would answer 400 to a value)', () => {
      accountingOn = false;
      edit(ROW);

      spectator.component.onSave();

      expect(input('tin')).toBeNull();
      expect(input('sstRegNo')).toBeNull();
      expect(saveCalls).toHaveLength(1);
      expect('tin' in body()).toBe(false);
      expect('sstRegNo' in body()).toBe(false);
    });

    it('a module list older than the row (another tab turned the module on): the identifiers on the row are kept, not cleared', () => {
      accountingOn = false;
      edit(ROW_WITH_TAX_IDS);

      spectator.component.form.controls.name.setValue('Acme Trading Sdn Bhd');
      spectator.component.onSave();

      expect(input('tin')?.value).toBe('C1234567890');
      expect(body()).toMatchObject({ tin: 'C1234567890', sstRegNo: 'W10-1808-32000001' });
    });
  });
});
