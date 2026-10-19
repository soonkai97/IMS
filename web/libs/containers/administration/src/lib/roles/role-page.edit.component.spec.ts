import { HttpContext } from '@angular/common/http';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of, throwError } from 'rxjs';
import { PermissionDto, PermissionGroup, PermissionsClient, RoleDto, RolesClient } from '@ims/api';
import { IF_MATCH } from '@ims/core/http';
import { provideUiText } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';
import { API_ERROR_KIND, ApiError, ApiErrorKind, PERMISSION } from '@ims/models';
import { RolePageComponent } from './role-page.component';

const ID = '00000000-0000-4000-8000-000000000011';

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

describe('RolePageComponent for a person who may change roles (RP02b: edit a role)', () => {
  let spectator: Spectator<RolePageComponent>;
  const held = signal<ReadonlySet<string>>(new Set());
  const ownRole = signal<string | undefined>('mine');
  const client = {
    get: jest.fn(),
    replacePermissions: jest.fn()
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
    spectator = createComponent({ props: { id } as never });
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
      expect(toast.success).toHaveBeenCalledWith(
        'Saved. 1 permission added, 0 permissions removed.'
      );
      expect(spectator.component.hasUnsavedChanges()).toBe(false);
    });

    it('does not call the API when nothing changed', async () => {
      await open(role());

      await save();

      expect(client.replacePermissions).not.toHaveBeenCalled();
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
      expect(spectator.queryAll('input').length).toBe(0);
      expect(spectator.query('[data-permission="roles.manage"]')?.textContent).toContain('Allowed');
    });

    it('a person without roles.manage gets the read-only list: no Save, no box', async () => {
      held.set(new Set(ALL.filter(code => code !== PERMISSION.RolesManage)));
      await open(role());

      expect(text()).toContain('You can see this role but not change it');
      expect(button('Save')).toBeUndefined();
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

    it('a view-only page never holds back the person who leaves it', async () => {
      held.set(new Set(ALL.filter(code => code !== PERMISSION.RolesManage)));
      await open(role());

      expect(spectator.component.hasUnsavedChanges()).toBe(false);
    });
  });
});
