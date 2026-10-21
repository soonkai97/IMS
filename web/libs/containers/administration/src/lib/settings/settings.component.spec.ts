import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { ApproverTier } from '@ims/api';
import { ApiError } from '@ims/models';
import { ModulesStore } from '@ims/core/identity';
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

describe('SettingsPageComponent approval rule tier (RP-4b: the rule says who approves by tier, never by role name)', () => {
  let spectator: Spectator<SettingsPageComponent>;
  const saveRun = jest.fn(() => of(undefined));
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
    spectator.component.onAddRule();
    spectator.component.ruleForm.controls.amountThreshold.setValue(100);
  });

  it('offers two plain-word levels and no Administrator option', () => {
    const labels = spectator.component.$tierOptions().map(option => option.label);

    expect(labels).toEqual(['Standard approvers', 'Large-document approvers']);
  });

  it.each([ApproverTier.Standard, ApproverTier.Large])(
    'saves %s as approverTier and sends no roleRequired',
    tier => {
      spectator.component.ruleForm.controls.approverTier.setValue(tier);

      spectator.component.onSaveRule();

      const [{ body }] = (
        saveRun.mock.calls as unknown as [{ body: Record<string, unknown> }][]
      )[0];
      expect(body['approverTier']).toBe(tier);
      expect('roleRequired' in body).toBe(false);
    }
  );
});

describe('SettingsPageComponent approval rule document types (module purchasing)', () => {
  const purchasingOn = signal(false);
  const createComponent = createComponentFactory({
    component: SettingsPageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      { provide: ToastService, useValue: { success: jest.fn() } },
      { provide: ModulesStore, useValue: { isEnabled: () => purchasingOn() } },
      {
        provide: SystemRulesStore,
        useValue: {
          settings: { $data: signal(undefined), $isLoading: signal(false), load: jest.fn() },
          rules: { $data: signal([]), load: jest.fn() },
          saveSettings: { $isRunning: signal(false), run: jest.fn() },
          saveRule: {
            $isRunning: signal(false),
            $operation: signal({}),
            run: jest.fn(),
            reset: jest.fn()
          }
        }
      }
    ]
  });
  const labels = (spectator: Spectator<SettingsPageComponent>): string[] =>
    spectator.component.$docTypeOptions().map(option => option.label);

  it('offers Purchase order only while the module is on', () => {
    const spectator = createComponent();

    purchasingOn.set(false);
    expect(labels(spectator)).toEqual(['Issue', 'Receipt']);

    purchasingOn.set(true);
    expect(labels(spectator)).toEqual(['Issue', 'Receipt', 'Purchase order']);
  });
});
