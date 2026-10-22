import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { throwError } from 'rxjs';
import { PagedResultOfPartnerDto, PartnerDto, PartnerType } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
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
  const listData = signal<PagedResultOfPartnerDto>({
    items: [ROW],
    page: 1,
    pageSize: 25,
    total: 1
  });
  const listLoading = signal(false);
  const createComponent = createComponentFactory({
    component: PartnersPageComponent,
    detectChanges: false,
    providers: [
      {
        provide: PermissionService,
        useValue: { has: (permission: string) => granted.has(permission) }
      },
      {
        provide: PartnersStore,
        useFactory: () => ({
          list: {
            $data: listData,
            $isLoading: listLoading,
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
    listLoading.set(false);
    listData.set({ items: [ROW], page: 1, pageSize: 25, total: 1 });
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

  describe('the save request (the accounting fields are gone, the API leaves stored values alone)', () => {
    const input = (name: string) =>
      spectator.query<HTMLInputElement>(`dialog app-input[formControlName="${name}"] input`);
    const body = () => (saveCalls[0] as { id: string; body: Record<string, unknown> }).body;
    const TAX_KEYS = ['tin', 'sstRegNo'];

    function edit(row: PartnerDto): void {
      render(PERMISSION.MasterDataRead, PERMISSION.MasterDataManage);
      spectator.component.onEdit(row);
      spectator.detectChanges();
    }

    it('an edit sends no tax number even when the API still returns them with the row', () => {
      edit(ROW_WITH_TAX_IDS);

      spectator.component.form.controls.name.setValue('Acme Trading Sdn Bhd');
      spectator.component.onSave();

      expect(saveCalls).toHaveLength(1);
      expect((saveCalls[0] as { id: string }).id).toBe(ROW.id);
      expect(body()).toMatchObject({ name: 'Acme Trading Sdn Bhd', isActive: true });
      for (const key of TAX_KEYS) {
        expect(key in body()).toBe(false);
      }
      expect(input('tin')).toBeNull();
      expect(input('sstRegNo')).toBeNull();
    });

    it('a new partner (a Clerk may create one) sends no tax number either', () => {
      render(PERMISSION.MasterDataRead, PERMISSION.PartnersCreate);
      spectator.component.onAdd();
      spectator.detectChanges();

      spectator.component.form.controls.name.setValue('New Co');
      spectator.component.onSave();

      expect(saveCalls).toHaveLength(1);
      expect(body()).toMatchObject({ name: 'New Co' });
      for (const key of TAX_KEYS) {
        expect(key in body()).toBe(false);
      }
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
  });
});
