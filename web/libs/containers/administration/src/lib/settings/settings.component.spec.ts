import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { ApiError } from '@ims/models';
import { ToastService } from '@ims/core/notification';
import { provideUiText } from '@ims/core/i18n';
import { SystemRulesStore } from '@ims/features/administration';
import { SettingsPageComponent } from './settings.component';

describe('SettingsPageComponent approval rule amount (R04: money has 2 decimals)', () => {
  let spectator: Spectator<SettingsPageComponent>;
  const saveRun = jest.fn(() => of(undefined));
  const operation = signal<{ error?: ApiError }>({});
  const createComponent = createComponentFactory({
    component: SettingsPageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      { provide: ToastService, useValue: { success: jest.fn() } },
      {
        provide: SystemRulesStore,
        useValue: {
          settings: { $data: signal(undefined), $isLoading: signal(false), load: jest.fn() },
          rules: { $data: signal([]), load: jest.fn() },
          saveSettings: { $isRunning: signal(false), run: jest.fn() },
          saveRule: {
            $isRunning: signal(false),
            $operation: operation,
            run: saveRun,
            reset: jest.fn()
          }
        }
      }
    ]
  });

  const amount = () => spectator.component.ruleForm.controls.amountThreshold;
  const text = () => spectator.element.textContent ?? '';

  beforeEach(() => {
    saveRun.mockClear();
    spectator = createComponent();
    spectator.detectChanges();
    spectator.component.onAddRule();
    spectator.detectChanges();
  });

  it('a 3rd decimal shows the message and blocks Save; nothing is sent or rounded', () => {
    amount().setValue(12.345);

    spectator.component.onSaveRule();
    spectator.detectChanges();

    expect(text()).toContain('Enter an amount with at most 2 decimals.');
    expect(amount().value).toBe(12.345);
    expect(saveRun).not.toHaveBeenCalled();
  });

  it('a pasted 12.3456 is blocked the same way', () => {
    amount().setValue(12.3456);

    spectator.component.onSaveRule();

    expect(saveRun).not.toHaveBeenCalled();
  });

  it.each([10, 10.5, 10.55, null])('%s is accepted and saved', value => {
    amount().setValue(value);

    spectator.component.onSaveRule();

    expect(saveRun).toHaveBeenCalledTimes(1);
  });

  it('a negative amount is refused', () => {
    amount().setValue(-1);

    spectator.component.onSaveRule();

    expect(amount().errors).toHaveProperty('min');
    expect(saveRun).not.toHaveBeenCalled();
  });
});
