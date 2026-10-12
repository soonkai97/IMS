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
  id: 1,
  type: PartnerType.Supplier,
  name: 'Acme Trading',
  contact: undefined,
  isActive: true
};

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
});
