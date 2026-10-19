import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { UserDto } from '@ims/api';
import { provideUiText } from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import { AssignableRolesStore, UsersStore } from '@ims/features/administration';
import { WarehousesStore } from '@ims/features/warehousing';
import { UsersPageComponent } from './users.component';

const CLERK = { code: 'Clerk', name: 'Clerk' };
const QA_ROLE = { code: 'QARoleA', name: 'QA role A' };

function user(role = QA_ROLE, overrides: Partial<UserDto> = {}): UserDto {
  return {
    id: 'u1',
    username: 'qa.user',
    email: 'qa@example.test',
    displayName: 'QA User',
    role: role.code,
    roleName: role.name,
    isActive: true,
    warehouseIds: ['w1'],
    allWarehouses: false,
    ...overrides
  };
}

describe('UsersPageComponent', () => {
  let spectator: Spectator<UsersPageComponent>;
  const saveRun = jest.fn(() => of(undefined));
  const rolesData = signal<{ code: string; name: string }[] | undefined>([CLERK, QA_ROLE]);
  const rolesFailed = signal(false);
  const rolesLoad = jest.fn();
  const rolesReload = jest.fn();
  const createComponent = createComponentFactory({
    component: UsersPageComponent,
    detectChanges: false,
    // The roles store is the page's own (providers on the component), so the test replaces it there.
    componentProviders: [
      {
        provide: AssignableRolesStore,
        useValue: {
          list: {
            $data: rolesData,
            $isFailed: rolesFailed,
            load: rolesLoad,
            reload: rolesReload
          }
        }
      }
    ],
    providers: [
      provideUiText(),
      { provide: ToastService, useValue: { success: jest.fn() } },
      {
        provide: WarehousesStore,
        useValue: {
          ensureLoaded: jest.fn(),
          $activeWarehouses: signal([{ id: 'w1', code: 'W1', name: 'Main' }])
        }
      },
      {
        provide: UsersStore,
        useValue: {
          list: {
            $data: signal(undefined),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load: jest.fn(),
            reload: jest.fn()
          },
          save: {
            $isRunning: signal(false),
            $operation: signal({}),
            run: saveRun,
            reset: jest.fn()
          }
        }
      }
    ]
  });

  const sentBody = () =>
    (
      saveRun.mock.calls as unknown as [
        { body: { role: string; allWarehouses: boolean; warehouseIds: string[] } }
      ][]
    )[0][0].body;
  const roleOptions = () =>
    spectator.component.$formRoleOptions().map(option => [option.value, option.label]);
  const fillNewUser = () => {
    spectator.component.onAdd();
    spectator.component.form.patchValue({
      username: 'ann',
      displayName: 'Ann',
      email: 'ann@example.test',
      role: 'Clerk',
      password: 'Passw0rd-1234'
    });
  };

  beforeEach(() => {
    saveRun.mockClear();
    rolesLoad.mockClear();
    rolesReload.mockClear();
    rolesData.set([CLERK, QA_ROLE]);
    rolesFailed.set(false);
    spectator = createComponent();
    spectator.detectChanges();
  });

  describe('roles come from the API', () => {
    it('asks the API for the assignable roles when the page opens', () => {
      expect(rolesLoad).toHaveBeenCalledTimes(1);
    });

    it('offers a custom role by name and only the roles the API lists', () => {
      spectator.component.onAdd();

      expect(roleOptions()).toEqual([
        ['Clerk', 'Clerk'],
        ['QARoleA', 'QA role A']
      ]);
    });

    it('does not offer a standard role the API did not list', () => {
      rolesData.set([QA_ROLE]);

      spectator.component.onAdd();

      expect(roleOptions()).toEqual([['QARoleA', 'QA role A']]);
    });

    it('filters the list by the same roles, by name', () => {
      expect(spectator.component.$roleOptions().map(option => option.label)).toEqual([
        'Clerk',
        'QA role A'
      ]);
    });

    it('shows the role name in the list, not the code', () => {
      const column = spectator.component.$columns().find(c => c.key === 'role');

      expect(column?.value?.(user())).toBe('QA role A');
    });

    it('lets a custom-role user be saved again with the custom role', () => {
      spectator.component.onEdit(user());
      expect(spectator.component.$roleLocked()).toBe(false);
      expect(spectator.component.form.controls.role.value).toBe('QARoleA');

      spectator.component.onSave();

      expect(sentBody().role).toBe('QARoleA');
    });

    it('sends a custom role picked for a new user', () => {
      fillNewUser();
      spectator.component.form.patchValue({ role: 'QARoleA', allWarehouses: true });

      spectator.component.onSave();

      expect(sentBody().role).toBe('QARoleA');
    });

    it('keeps a role the caller cannot assign: shown by name, locked, and sent unchanged', () => {
      rolesData.set([CLERK]);

      spectator.component.onEdit(user());
      spectator.detectChanges();

      expect(spectator.component.$roleLocked()).toBe(true);
      expect(spectator.component.form.controls.role.disabled).toBe(true);
      expect(roleOptions()).toContainEqual(['QARoleA', 'QA role A']);
      spectator.component.onSave();
      expect(sentBody().role).toBe('QARoleA');
    });

    it('locks nothing and keeps the current role when the roles could not be loaded', () => {
      rolesData.set(undefined);
      rolesFailed.set(true);

      spectator.component.onEdit(user());
      spectator.detectChanges();

      expect(spectator.component.$roleLocked()).toBe(false);
      expect(roleOptions()).toEqual([['QARoleA', 'QA role A']]);
      expect(spectator.component.$rolesFailed()).toBe(true);
    });

    it('asks for the roles again on retry', () => {
      spectator.component.onRolesRetry();

      expect(rolesReload).toHaveBeenCalledTimes(1);
    });
  });

  describe('scope (RP-4b: "sees every warehouse" is a choice per person, sent with the save)', () => {
    beforeEach(() => fillNewUser());

    it('sends allWarehouses true as ticked', () => {
      spectator.component.form.controls.allWarehouses.setValue(true);

      spectator.component.onSave();

      expect(sentBody().allWarehouses).toBe(true);
    });

    it('sends allWarehouses false with the ticked warehouse', () => {
      spectator.component.$warehouseChoices()[0].control.setValue(true);

      spectator.component.onSave();

      expect(sentBody().allWarehouses).toBe(false);
      expect(sentBody().warehouseIds).toEqual(['w1']);
    });

    it('starts a new user without the flag', () => {
      expect(spectator.component.form.controls.allWarehouses.value).toBe(false);
    });
  });

  describe('a user who would see no warehouse', () => {
    beforeEach(() => fillNewUser());

    it('is not saved, and the sentence says what to do', () => {
      spectator.component.onSave();
      spectator.detectChanges();

      expect(saveRun).not.toHaveBeenCalled();
      expect(spectator.component.$noWarehouse()).toBe(true);
      expect(spectator.query('.users-page-warehouses [role="alert"]')?.textContent).toContain(
        'This user will see no warehouse. Pick at least one or tick Sees every warehouse.'
      );
    });

    it('is saved once a warehouse is ticked', () => {
      spectator.component.onSave();
      spectator.component.$warehouseChoices()[0].control.setValue(true);

      spectator.component.onSave();

      expect(saveRun).toHaveBeenCalledTimes(1);
      expect(spectator.component.$noWarehouse()).toBe(false);
    });

    it('is saved once "sees every warehouse" is ticked', () => {
      spectator.component.onSave();
      spectator.component.form.controls.allWarehouses.setValue(true);

      spectator.component.onSave();

      expect(saveRun).toHaveBeenCalledTimes(1);
    });

    it('also stops an existing user whose boxes were all cleared', () => {
      spectator.component.onEdit(user(QA_ROLE, { warehouseIds: [] }));

      spectator.component.onSave();

      expect(saveRun).not.toHaveBeenCalled();
    });

    it('clears the sentence when the person ticks a box', () => {
      spectator.component.onSave();
      spectator.detectChanges();

      spectator.dispatchFakeEvent('.users-page-warehouses', 'change');

      expect(spectator.component.$noWarehouse()).toBe(false);
    });
  });
});
