import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { provideUiText } from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import { UsersStore } from '@ims/features/administration';
import { WarehousesStore } from '@ims/features/warehousing';
import { UsersPageComponent } from './users.component';

describe('UsersPageComponent scope (RP-4b: "sees every warehouse" is a choice per person, sent with the save)', () => {
  let spectator: Spectator<UsersPageComponent>;
  const saveRun = jest.fn(() => of(undefined));
  const createComponent = createComponentFactory({
    component: UsersPageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      { provide: ToastService, useValue: { success: jest.fn() } },
      {
        provide: WarehousesStore,
        useValue: { ensureLoaded: jest.fn(), $activeWarehouses: signal([]) }
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

  beforeEach(() => {
    saveRun.mockClear();
    spectator = createComponent();
    spectator.detectChanges();
    spectator.component.onAdd();
    spectator.component.form.patchValue({
      username: 'ann',
      displayName: 'Ann',
      email: 'ann@example.test',
      role: 'Clerk',
      password: 'Passw0rd-1234'
    });
  });

  it.each([true, false])('sends allWarehouses %s as ticked', value => {
    spectator.component.form.controls.allWarehouses.setValue(value);

    spectator.component.onSave();

    const [{ body }] = (
      saveRun.mock.calls as unknown as [{ body: { allWarehouses: boolean } }][]
    )[0];
    expect(body.allWarehouses).toBe(value);
  });

  it('starts a new user without the flag', () => {
    expect(spectator.component.form.controls.allWarehouses.value).toBe(false);
  });
});
