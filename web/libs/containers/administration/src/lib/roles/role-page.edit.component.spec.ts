import { HttpContext } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { NEVER, of, throwError } from 'rxjs';
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

describe('RolePageComponent for a person who may change roles (RP02b: edit a role)', () => {
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
  const catalogList = jest.fn();
  const toast = { success: jest.fn(), error: jest.fn(), dismissErrors: jest.fn() };
  const createComponent = createComponentFactory({
    component: RolePageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      provideRouter([]),
      { provide: RolesClient, useValue: client },
      { provide: PermissionsClient, useValue: { list: catalogList } },
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
    catalogList.mockReturnValue(of(CATALOG));
    client.list.mockReturnValue(of([role()]));
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
    });

    it('names a box by the permission alone and describes it by separate sentences', async () => {
      await open(role());

      const input = box('document.reverse');
      expect(input.disabled).toBe(true);
      expect(box('stock.read').disabled).toBe(false);
      const description = spectator.query(
        `[id="${input.getAttribute('aria-describedby')}"]`
      ) as HTMLElement;
      expect(input.closest('label')?.textContent?.trim()).toBe('Reverse posted documents');
      expect(description.textContent).toContain('High impact');
      expect(description.textContent).toContain('Needs: Post documents');
      expect(description.textContent).toContain('Reverse a posted document, with a reason.');
      expect(description.textContent).toContain("You can't give a permission you don't have.");
      expect(description.children.length).toBeGreaterThanOrEqual(3);
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
      expect(toast.success).toHaveBeenCalledTimes(1);
      expect(spectator.query('[role="status"]')?.textContent?.trim()).toBe('');
      expect(spectator.component.hasUnsavedChanges()).toBe(false);
    });

    it('a second click on Save while the first is on its way sends nothing more', async () => {
      await open(role());
      client.replacePermissions.mockReturnValue(NEVER);

      await tick('document.post');
      const saves = spectator
        .queryAll<HTMLButtonElement>('button')
        .filter(b => b.textContent?.trim() === 'Save');
      expect(saves.length).toBe(2);
      saves[0].click();
      saves[1].click();
      await settle();

      expect(client.replacePermissions).toHaveBeenCalledTimes(1);
    });
  });

  describe('the text of a role', () => {
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

    it('a name that is taken lands on the Name field when editing too', async () => {
      await open(role());
      client.update.mockReturnValue(
        throwError(() => conflict("A role named 'Clerk' already exists."))
      );

      spectator.typeInElement('Clerk', 'app-input[formControlName="name"] input');
      await save();

      expect(text()).toContain("A role named 'Clerk' already exists.");
      expect(banners().join(' ')).not.toContain('already exists');
    });

    it('Save waits for a change: disabled while nothing differs, enabled by a tick', async () => {
      await open(role());
      const saves = () =>
        spectator
          .queryAll<HTMLButtonElement>('button')
          .filter(b => b.textContent?.trim() === 'Save');
      expect(saves().every(b => b.disabled)).toBe(true);

      await tick('document.post');

      expect(saves().every(b => !b.disabled)).toBe(true);
    });

    it('a Save the form refuses puts the focus on the first field to fix', async () => {
      await open(role());
      spectator.typeInElement('', 'app-input[formControlName="name"] input');
      await settle();

      await save();

      expect(document.activeElement).toBe(
        spectator.query('app-input[formControlName="name"] input')
      );
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

  describe('the text is saved but the permissions are refused', () => {
    const renameAndTick = async (error: ApiError): Promise<void> => {
      client.get.mockReset();
      client.get.mockReturnValue(of(role()));
      await open(undefined, ID);
      client.update.mockReturnValue(of(role({ name: 'Late clerk', concurrencyToken: 'v2' })));
      client.replacePermissions.mockReturnValue(throwError(() => error));

      spectator.typeInElement('Late clerk', 'app-input[formControlName="name"] input');
      await tick('document.post');
      await save();
    };

    it.each([
      [
        'a 403',
        problem(
          403,
          'You cannot change a role that holds more permissions than you.',
          API_ERROR_KIND.Forbidden
        ),
        'holds more permissions than you'
      ],
      ['a lost connection', problem(0, 'Network down.', API_ERROR_KIND.Network), 'Network down.']
    ])(
      'says the text was saved and why the permissions were not: %s',
      async (_label, error, reason) => {
        await renameAndTick(error);

        const shown = banners().join(' ');
        expect(shown).toContain('Name and description were saved. The permissions were not:');
        expect(shown).toContain(reason);
        // The saved text is the new baseline and the ticks are still there to retry; one alert, no toast.
        expect(spectator.query('h1')?.textContent).toContain('Late clerk');
        expect(box('document.post').checked).toBe(true);
        expect(toast.success).not.toHaveBeenCalled();
        expect(spectator.queryAll('app-banner[role="alert"]').length).toBe(1);
      }
    );

    it('a stale 409 on the permissions keeps the saved text in the message and offers Reload', async () => {
      await renameAndTick(conflict('The role was changed by someone else.'));

      expect(banners().join(' ')).toContain('Name and description were saved.');
      expect(banners().join(' ')).toContain('Someone changed this role');
      expect(button('Reload the role')).toBeDefined();
    });

    it('only the ticks count as unsaved afterwards: the text is not asked about again', async () => {
      await renameAndTick(problem(403, 'x', API_ERROR_KIND.Forbidden));
      client.replacePermissions.mockReturnValue(
        of({
          role: role({ name: 'Late clerk', permissions: ['stock.read', 'document.post'] }),
          added: ['document.post'],
          removed: []
        })
      );

      await save();

      expect(client.update).toHaveBeenCalledTimes(1);
      expect(client.replacePermissions).toHaveBeenCalledTimes(2);
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

    it.each([
      'You can only grant permissions you hold yourself.',
      'You cannot change a role that holds more permissions than you.',
      'You cannot change a role that is held by a user who sees more warehouses than you.'
    ])('a 403 shows the server sentence for its own cause: %s', async sentence => {
      // The error interceptor replaces `message` by its generic sentence; the page shows the server's.
      const refused = { ...problem(403, sentence, API_ERROR_KIND.Forbidden), message: 'Generic.' };
      await failSave(refused);

      expect(banners().join(' ')).toContain(sentence);
      expect(spectator.component.hasUnsavedChanges()).toBe(true);
    });

    it('retiring a role that people still have is refused with the number of people', async () => {
      client.get.mockReset();
      client.get.mockReturnValue(of(role({ userCount: 2 })));
      await open(undefined, ID);
      client.update.mockReturnValue(
        throwError(() =>
          conflict('2 user(s) still have this role. Move them to another role first.')
        )
      );

      spectator.click(spectator.query('app-checkbox[formControlName="isActive"] input') as Element);
      await save();

      expect(banners().join(' ')).toContain('2 people have this role. Move them to another role');
      expect(button('Reload the role')).toBeUndefined();
    });

    it('a 404 turns the page into "not found" (the role was deleted meanwhile)', async () => {
      await failSave(problem(404, 'Role not found.', API_ERROR_KIND.NotFound));

      expect(text()).toContain("This page or document doesn't exist");
      expect(spectator.query('form')).toBeNull();
    });
  });

  describe('who can change a role', () => {
    const CORE = ['users.read', 'users.manage', 'roles.read', 'roles.manage'];
    it.each([
      [
        'the locked role',
        role({ isSystem: true, permissions: CORE }),
        'cannot be changed',
        'mine',
        true
      ],
      ['a role seen without roles.manage', role(), 'but not change it', 'mine', false],
      ['their own role', role(), 'your own role', 'night', true],
      [
        'a role that holds more than they do',
        role({ permissions: ['document.reverse'] }),
        'permissions you do not have',
        'mine',
        true
      ]
    ])(
      '%s is the read-only list: no Save, no box, the reason in words',
      async (_label, current, reason, ownCode, canManage) => {
        if (!canManage) {
          held.set(new Set(ALL.filter(code => code !== PERMISSION.RolesManage)));
        }
        ownRole.set(ownCode);
        await open(current);

        expect(text()).toContain(reason);
        expect(button('Save')).toBeUndefined();
        expect(spectator.queryAll('input').length).toBe(0);
        expect(spectator.component.hasUnsavedChanges()).toBe(false);
      }
    );
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
      // The dialog is gone: the focus is back on the button that opened it, not on the body.
      expect(document.activeElement).toBe(button('Delete role'));
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
      expect(navigate).toHaveBeenCalledWith(['/roles', OTHER], {
        replaceUrl: true,
        scroll: 'manual'
      });
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

    it('changing Start from asks first when ticks were made by hand, and a refusal keeps them', async () => {
      client.list.mockReturnValue(
        of([role({ id: OTHER, name: 'Source', permissions: ['stock.read', 'document.post'] })])
      );
      await open(undefined);
      await tick('stock.read');

      spectator.component.form.controls.copyFromId.setValue(OTHER);
      await settle();
      TestBed.inject(ConfirmService).cancel();
      await settle();

      expect(box('document.post').checked).toBe(false);
      expect(spectator.component.form.controls.copyFromId.value).toBeNull();

      spectator.component.form.controls.copyFromId.setValue(OTHER);
      await settle();
      TestBed.inject(ConfirmService).confirm('');
      await settle();

      expect(box('document.post').checked).toBe(true);
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
      expect(document.activeElement).toBe(spectator.query('h1'));
    });

    it('leaving the page by closing it asks first only while there are unsaved ticks', async () => {
      await open(role());
      const leave = () => {
        const event = new Event('beforeunload', { cancelable: true });
        window.dispatchEvent(event);
        return event.defaultPrevented;
      };
      expect(leave()).toBe(false);

      await tick('document.post');

      expect(leave()).toBe(true);
    });
  });
});
