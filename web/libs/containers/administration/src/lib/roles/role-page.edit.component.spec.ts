import { HttpContext } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of, throwError } from 'rxjs';
import { PermissionDto, PermissionGroup, PermissionsClient, RoleDto, RolesClient } from '@ims/api';
import { IF_MATCH } from '@ims/core/http';
import { provideUiText } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { API_ERROR_KIND, ApiError, ApiErrorKind, PERMISSION } from '@ims/models';
import { RolePageComponent } from './role-page.component';

const ID = '00000000-0000-4000-8000-000000000011';
const OTHER = '00000000-0000-4000-8000-000000000012';

const permission = (
  code: string,
  group: PermissionGroup,
  requires: string[] = [],
  extra: Partial<PermissionDto> = {}
): PermissionDto => ({
  code,
  group,
  description: `${code} help`,
  isDangerous: false,
  adminCore: false,
  requires,
  ...extra
});
const CATALOG: PermissionDto[] = [
  permission('users.read', PermissionGroup.Administration, [], { adminCore: true }),
  permission('users.manage', PermissionGroup.Administration, ['users.read'], { adminCore: true }),
  permission('roles.read', PermissionGroup.Administration, [], { adminCore: true }),
  permission('roles.manage', PermissionGroup.Administration, ['roles.read'], {
    adminCore: true,
    isDangerous: true
  }),
  permission('stock.read', PermissionGroup.StockAndReports),
  permission('document.post', PermissionGroup.Documents, ['stock.read']),
  permission('document.reverse', PermissionGroup.Documents, ['document.post'], {
    isDangerous: true
  })
];
const ALL = CATALOG.map(p => p.code);
const role = (extra: Partial<RoleDto> = {}): RoleDto => ({
  id: ID,
  code: 'night',
  name: 'Night clerk',
  description: 'Works at night',
  isSystem: false,
  isActive: true,
  permissions: ['stock.read'],
  userCount: 0,
  concurrencyToken: 'v1',
  ...extra
});
const problem = (
  status: number,
  detail: string,
  kind: ApiErrorKind,
  fieldErrors: Record<string, string[]> = {}
): ApiError => ({
  isApiError: true,
  kind,
  status,
  title: '',
  detail,
  message: detail,
  fieldErrors,
  extensions: {}
});
const conflict = (detail: string) => problem(409, detail, API_ERROR_KIND.Conflict);
const tokenOf = (context: HttpContext | undefined) => context?.get(IF_MATCH);

describe('RolePageComponent (RP02: create and edit a role)', () => {
  let spectator: Spectator<RolePageComponent>;
  const held = signal<ReadonlySet<string>>(new Set());
  const ownRole = signal<string | undefined>('mine');
  const client = {
    get: jest.fn(),
    list: jest.fn(),
    create: jest.fn(),
    update: jest.fn(),
    replacePermissions: jest.fn(),
    delete: jest.fn()
  };
  const toast = { success: jest.fn(), error: jest.fn(), dismissErrors: jest.fn() };
  const createComponent = createComponentFactory({
    component: RolePageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      provideRouter([]),
      { provide: RolesClient, useValue: client },
      { provide: PermissionsClient, useValue: { list: () => of(CATALOG) } },
      { provide: ToastService, useValue: toast },
      {
        provide: PermissionService,
        useValue: {
          has: (code: string) => held().has(code),
          $permissions: held,
          $role: ownRole
        }
      }
    ]
  });

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      spectator.detectChanges();
      await spectator.fixture.whenStable();
    }
  }

  async function open(
    current: RoleDto | undefined,
    id: string | undefined = current?.id
  ): Promise<void> {
    if (current) {
      client.get.mockReturnValue(of(current));
    }
    spectator = createComponent({ props: (id ? { id } : {}) as never });
    await settle();
  }

  const row = (code: string) => spectator.query(`[data-permission="${code}"]`) as HTMLElement;
  const box = (code: string) => row(code).querySelector('input') as HTMLInputElement;
  const tick = async (code: string) => {
    spectator.click(box(code));
    await settle();
  };
  const button = (label: string) =>
    spectator.queryAll<HTMLButtonElement>('button').find(b => b.textContent?.trim() === label);
  const save = async () => {
    button('Save')?.click();
    await settle();
  };
  const banners = () => spectator.queryAll('app-banner').map(b => b.textContent?.trim() ?? '');
  const text = () => spectator.element.textContent ?? '';

  beforeEach(() => {
    jest.resetAllMocks();
    held.set(new Set([...ALL.filter(code => code !== 'document.reverse'), PERMISSION.RolesManage]));
    ownRole.set('mine');
    client.list.mockReturnValue(of([role()]));
  });

  describe('the permission list', () => {
    it('lists the permissions by section, one fieldset with a legend each, ticked as the role holds them', async () => {
      await open(role());

      const legends = spectator.queryAll('fieldset > legend h3').map(h => h.textContent?.trim());
      expect(legends).toEqual(['Administration', 'Stock and reports', 'Documents']);
      expect(box('stock.read').checked).toBe(true);
      expect(box('document.post').checked).toBe(false);
      expect(row('document.reverse').textContent).toContain('High impact');
      expect(row('document.post').textContent).toContain('Needs: See stock');
      expect(row('stock.read').textContent).toContain(
        'See stock levels, the ledger and the low-stock list.'
      );
    });

    it('turning a permission on also turns on what it needs, and says so once in the live region', async () => {
      await open(role({ permissions: [] }));

      await tick('document.post');

      expect(box('document.post').checked).toBe(true);
      expect(box('stock.read').checked).toBe(true);
      expect(spectator.query('[role="status"]')?.textContent).toContain(
        'Also turned on: See stock'
      );
    });

    it('turning a permission off also turns off what needs it', async () => {
      await open(role({ permissions: ['stock.read', 'document.post'] }));

      await tick('stock.read');

      expect(box('stock.read').checked).toBe(false);
      expect(box('document.post').checked).toBe(false);
      expect(spectator.query('[role="status"]')?.textContent).toContain('Also turned off');
    });

    it('shows a permission the person does not hold disabled, with the reason in words', async () => {
      await open(role());

      expect(box('document.reverse').disabled).toBe(true);
      expect(row('document.reverse').textContent).toContain(
        "You can't give a permission you don't have."
      );
      expect(box('stock.read').disabled).toBe(false);
    });
  });

  describe('saving an existing role', () => {
    it('sends the whole set with the concurrency token as If-Match, then shows what changed', async () => {
      await open(role());
      client.replacePermissions.mockReturnValue(
        of({
          role: role({ permissions: ['stock.read', 'document.post'], concurrencyToken: 'v2' }),
          added: ['document.post'],
          removed: []
        })
      );

      await tick('document.post');
      await save();

      expect(client.replacePermissions).toHaveBeenCalledTimes(1);
      const [id, body, context] = client.replacePermissions.mock.calls[0];
      expect(id).toBe(ID);
      expect([...body.permissions].sort()).toEqual(['document.post', 'stock.read']);
      expect(tokenOf(context)).toBe('v1');
      expect(client.update).not.toHaveBeenCalled();
      expect(toast.success).toHaveBeenCalledWith(
        'Saved. 1 permission added, 0 permissions removed.'
      );
      expect(spectator.component.hasUnsavedChanges()).toBe(false);
    });

    it('saves the text first and sends the permissions with the token that save returned', async () => {
      await open(role());
      client.update.mockReturnValue(of(role({ name: 'Late clerk', concurrencyToken: 'v2' })));
      client.replacePermissions.mockReturnValue(
        of({
          role: role({ name: 'Late clerk', permissions: [], concurrencyToken: 'v3' }),
          added: [],
          removed: ['stock.read']
        })
      );

      spectator.typeInElement('Late clerk', 'app-input[formControlName="name"] input');
      await tick('stock.read');
      await save();

      expect(tokenOf(client.update.mock.calls[0][2])).toBe('v1');
      expect(client.update.mock.calls[0][1]).toEqual({
        name: 'Late clerk',
        description: 'Works at night',
        isActive: true
      });
      expect(tokenOf(client.replacePermissions.mock.calls[0][2])).toBe('v2');
    });

    it('does not call the API when nothing changed', async () => {
      await open(role());

      await save();

      expect(client.update).not.toHaveBeenCalled();
      expect(client.replacePermissions).not.toHaveBeenCalled();
    });

    it('a standard role keeps its text: only its permissions are sent', async () => {
      await open(role({ isSystem: true, name: 'Supervisor', code: 'standard' }));
      client.replacePermissions.mockReturnValue(
        of({ role: role({ isSystem: true, permissions: [] }), added: [], removed: ['stock.read'] })
      );

      expect(
        spectator.query<HTMLInputElement>('app-input[formControlName="name"] input')?.disabled
      ).toBe(true);
      expect(text()).toContain('A standard role keeps its name');
      await tick('stock.read');
      await save();

      expect(client.update).not.toHaveBeenCalled();
      expect(client.replacePermissions).toHaveBeenCalledTimes(1);
    });
  });

  describe('refusals are explained in words', () => {
    async function failSave(error: ApiError): Promise<void> {
      await open(role());
      client.replacePermissions.mockReturnValue(throwError(() => error));
      await tick('document.post');
      await save();
    }

    it('a 409 on a role someone else changed says so and offers a reload that discards the edits', async () => {
      client.get.mockReset();
      client.get.mockReturnValueOnce(of(role()));
      await open(undefined, ID);
      client.get.mockReturnValue(of(role({ concurrencyToken: 'v2' })));
      client.replacePermissions.mockReturnValue(
        throwError(() => conflict('The role was changed by someone else. Reload it and try again.'))
      );

      await tick('document.post');
      await save();

      expect(banners().join(' ')).toContain('Someone changed this role while you were editing');
      expect(button('Reload the role')).toBeDefined();

      client.get.mockReturnValue(of(role({ concurrencyToken: 'v2', permissions: ['stock.read'] })));
      button('Reload the role')?.click();
      await settle();

      expect(banners().join(' ')).not.toContain('Someone changed');
      expect(box('document.post').checked).toBe(false);
      expect(spectator.component.hasUnsavedChanges()).toBe(false);
    });

    it('a 409 on a role nobody changed is not called a stale edit: the server sentence is shown', async () => {
      client.get.mockReset();
      client.get.mockReturnValue(of(role()));
      await open(undefined, ID);
      client.replacePermissions.mockReturnValue(
        throwError(() =>
          conflict('The Administrator role always holds every permission and cannot be changed.')
        )
      );

      await tick('document.post');
      await save();

      const shown = banners().join(' ');
      expect(shown).toContain('cannot be changed');
      expect(shown).not.toContain('Someone changed');
      expect(button('Reload the role')).toBeUndefined();
    });

    it('a 403 explains the containment rule', async () => {
      await failSave(problem(403, 'x', API_ERROR_KIND.Forbidden));

      expect(banners().join(' ')).toContain('You can only give permissions you hold yourself');
      expect(spectator.component.hasUnsavedChanges()).toBe(true);
    });

    it('a 400 shows the server sentence', async () => {
      await failSave(
        problem(400, "'document.post' also needs 'stock.read'.", API_ERROR_KIND.Validation)
      );

      expect(banners().join(' ')).toContain("also needs 'stock.read'");
    });

    it('a 404 turns the page into "not found" (the role was deleted meanwhile)', async () => {
      await failSave(problem(404, 'Role not found.', API_ERROR_KIND.NotFound));

      expect(text()).toContain("This page or document doesn't exist");
      expect(spectator.query('form')).toBeNull();
    });
  });

  describe('who can change a role', () => {
    it('the locked role is the read-only list: no Save, no box, the reason in words', async () => {
      await open(
        role({
          isSystem: true,
          permissions: ['users.read', 'users.manage', 'roles.read', 'roles.manage']
        })
      );

      expect(text()).toContain('always holds every permission and cannot be changed');
      expect(button('Save')).toBeUndefined();
      expect(button('Delete role')).toBeUndefined();
      expect(spectator.queryAll('input').length).toBe(0);
      expect(spectator.query('[data-permission="roles.manage"]')?.textContent).toContain('Allowed');
    });

    it('a person without roles.manage gets the read-only list: no Save, no Delete, no box', async () => {
      held.set(new Set(ALL.filter(code => code !== PERMISSION.RolesManage)));
      await open(role());

      expect(text()).toContain('You can see this role but not change it');
      expect(button('Save')).toBeUndefined();
      expect(button('Delete role')).toBeUndefined();
      expect(spectator.queryAll('input').length).toBe(0);
    });

    it('a person cannot change the role they hold', async () => {
      ownRole.set('night');
      await open(role());

      expect(text()).toContain('your own role');
      expect(button('Save')).toBeUndefined();
      expect(spectator.queryAll('input').length).toBe(0);
    });

    it('a person cannot change a role that holds more than they do', async () => {
      await open(role({ permissions: ['stock.read', 'document.reverse'] }));

      expect(text()).toContain('holds permissions you do not have');
      expect(button('Save')).toBeUndefined();
      expect(spectator.queryAll('input').length).toBe(0);
    });
  });

  describe('deleting', () => {
    const confirmDelete = async () => {
      button('Delete role')?.click();
      await settle();
      TestBed.inject(ConfirmService).confirm('');
      await settle();
    };

    it('asks first, then deletes and goes back to the list', async () => {
      await open(role());
      client.delete.mockReturnValue(of(undefined));
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      await confirmDelete();

      expect(client.delete).toHaveBeenCalledWith(ID, expect.anything());
      expect(navigate).toHaveBeenCalledWith(['/roles']);
    });

    it('does nothing when the person keeps the role', async () => {
      await open(role());
      button('Delete role')?.click();
      await settle();
      TestBed.inject(ConfirmService).cancel();
      await settle();

      expect(client.delete).not.toHaveBeenCalled();
    });

    it('a role in use is refused with the count of people', async () => {
      await open(role({ userCount: 3 }));
      client.delete.mockReturnValue(
        throwError(() =>
          conflict('3 user(s) still have this role. Move them to another role first.')
        )
      );

      await confirmDelete();

      expect(banners().join(' ')).toContain('3 people have this role. Move them to another role');
      expect(button('Delete role')).toBeDefined();
    });

    it('a standard role has no Delete button', async () => {
      await open(role({ isSystem: true }));

      expect(button('Delete role')).toBeUndefined();
    });
  });

  describe('creating a role', () => {
    it('needs a name before it calls the API', async () => {
      await open(undefined);

      await save();

      expect(client.create).not.toHaveBeenCalled();
      expect(text()).toContain('required');
    });

    it('sends the ticked permissions and opens the new role', async () => {
      await open(undefined);
      client.create.mockReturnValue(of(role({ id: OTHER, name: 'Auditor' })));
      const navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      spectator.typeInElement('Auditor', 'app-input[formControlName="name"] input');
      await tick('document.post');
      await save();

      expect(client.create).toHaveBeenCalledTimes(1);
      const body = client.create.mock.calls[0][0];
      expect(body.name).toBe('Auditor');
      expect([...body.permissions].sort()).toEqual(['document.post', 'stock.read']);
      expect(body.copyFromRoleId).toBeUndefined();
      expect(navigate).toHaveBeenCalledWith(['/roles', OTHER], { replaceUrl: true });
    });

    it('copying a role fills the ticks, and an untouched copy is sent as a copy', async () => {
      client.list.mockReturnValue(
        of([role({ id: OTHER, name: 'Source', permissions: ['stock.read', 'document.post'] })])
      );
      await open(undefined);
      client.create.mockReturnValue(of(role({ id: ID })));
      jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      spectator.component.form.controls.copyFromId.setValue(OTHER);
      spectator.typeInElement('Copy', 'app-input[formControlName="name"] input');
      await settle();
      expect(box('document.post').checked).toBe(true);
      await save();

      const body = client.create.mock.calls[0][0];
      expect(body.copyFromRoleId).toBe(OTHER);
      expect(body.permissions).toBeUndefined();
    });

    it('a copy that was edited is sent as a list', async () => {
      client.list.mockReturnValue(
        of([role({ id: OTHER, name: 'Source', permissions: ['stock.read', 'document.post'] })])
      );
      await open(undefined);
      client.create.mockReturnValue(of(role({ id: ID })));
      jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      spectator.component.form.controls.copyFromId.setValue(OTHER);
      spectator.typeInElement('Copy', 'app-input[formControlName="name"] input');
      await settle();
      await tick('document.post');
      await save();

      const body = client.create.mock.calls[0][0];
      expect(body.copyFromRoleId).toBeUndefined();
      expect(body.permissions).toEqual(['stock.read']);
    });

    it('does not offer a role that holds more than the person (it would be refused)', async () => {
      client.list.mockReturnValue(
        of([
          role({ id: OTHER, name: 'Too much', permissions: ['document.reverse'] }),
          role({ id: ID, name: 'Fine' })
        ])
      );
      await open(undefined);

      const options = spectator.component.$copyOptions();
      expect(options.find(o => o.label.startsWith('Too much'))?.disabled).toBe(true);
      expect(options.find(o => o.label === 'Fine')?.disabled).toBe(false);
    });

    it('a name that is taken lands on the Name field', async () => {
      await open(undefined);
      client.create.mockReturnValue(
        throwError(() => conflict("A role named 'Auditor' already exists."))
      );

      spectator.typeInElement('Auditor', 'app-input[formControlName="name"] input');
      await save();

      expect(text()).toContain("A role named 'Auditor' already exists.");
      expect(banners().join(' ')).not.toContain('already exists');
    });
  });

  describe('unsaved changes', () => {
    it('a tick makes the page dirty; Discard puts the saved set back', async () => {
      await open(role());
      expect(spectator.component.hasUnsavedChanges()).toBe(false);

      await tick('document.post');
      expect(spectator.component.hasUnsavedChanges()).toBe(true);

      button('Discard changes')?.click();
      await settle();

      expect(spectator.component.hasUnsavedChanges()).toBe(false);
      expect(box('document.post').checked).toBe(false);
    });

    it('typing in the name makes the page dirty', async () => {
      await open(role());

      spectator.typeInElement('Other', 'app-input[formControlName="name"] input');
      await settle();

      expect(spectator.component.hasUnsavedChanges()).toBe(true);
    });

    it('a view-only page never holds back the person who leaves it', async () => {
      held.set(new Set(ALL.filter(code => code !== PERMISSION.RolesManage)));
      await open(role());

      expect(spectator.component.hasUnsavedChanges()).toBe(false);
    });
  });
});
