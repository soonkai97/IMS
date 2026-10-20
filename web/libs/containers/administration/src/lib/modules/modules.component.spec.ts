import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { EMPTY, of } from 'rxjs';
import { ModuleDto, ModuleSwitchResult } from '@ims/api';
import { provideUiText } from '@ims/core/i18n';
import { ModulesStore, PermissionService } from '@ims/core/identity';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { ApiError, HTTP_STATUS, MIN_REASON_LENGTH, PERMISSION } from '@ims/models';
import { ModulesPageComponent } from './modules.component';

const moduleOf = (code: string, enabled: boolean, requires: string[] = []): ModuleDto => ({
  code,
  name: code === 'accounting' ? 'Accounting' : `Module ${code}`,
  description: `${code} description`,
  enabled,
  requires
});

const result = (
  code: string,
  enabled: boolean,
  rolesRegainingGrants: number
): ModuleSwitchResult => ({
  ...moduleOf(code, enabled),
  rolesRegainingGrants
});

describe('ModulesPageComponent (Settings > Modules)', () => {
  const data = signal<ModuleDto[] | undefined>(undefined);
  const operation = signal<{ error?: Partial<ApiError> }>({});
  const load = jest.fn();
  const run = jest.fn();
  const reset = jest.fn();
  const ask = jest.fn();
  const success = jest.fn();
  const held = new Set<string>();
  let spectator: Spectator<ModulesPageComponent>;

  const createComponent = createComponentFactory({
    component: ModulesPageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      provideRouter([]),
      { provide: ToastService, useValue: { success } },
      { provide: ConfirmService, useValue: { ask } },
      { provide: PermissionService, useValue: { has: (code: string) => held.has(code) } },
      {
        provide: ModulesStore,
        useValue: {
          list: {
            $data: data,
            $isLoading: signal(false),
            $isFailed: signal(false),
            load
          },
          switchModule: { $isRunning: signal(false), $operation: operation, run, reset }
        }
      }
    ]
  });

  const text = () => spectator.element.textContent ?? '';
  const switches = () => spectator.queryAll<HTMLButtonElement>('button[role="switch"]');
  const banner = () => spectator.query('app-banner')?.textContent?.trim();

  beforeEach(() => {
    jest.clearAllMocks();
    held.clear();
    data.set([moduleOf('accounting', false)]);
    operation.set({});
    reset.mockImplementation(() => operation.set({}));
    spectator = createComponent();
    spectator.detectChanges();
  });

  it('lists each module with its name, description and state in words, and loads the list from the server', () => {
    expect(load).toHaveBeenCalledTimes(1);
    expect(text()).toContain('Accounting');
    expect(text()).toContain('Tax-ready fields on products and partners');
    expect(switches()).toHaveLength(1);
    expect(switches()[0].getAttribute('aria-checked')).toBe('false');
    expect(text()).toContain('Off');
  });

  it('shows what a module requires, by name', () => {
    data.set([moduleOf('accounting', true), moduleOf('batch', false, ['accounting'])]);
    spectator.detectChanges();

    expect(text()).toContain('Requires: Accounting');
  });

  describe('accounting is not switchable on until PL1.8 (plan risk 11)', () => {
    it('shows a switch that is off, cannot be used, and says why in words', () => {
      const control = switches()[0];

      expect(control.getAttribute('aria-disabled')).toBe('true');
      expect(control.getAttribute('aria-describedby')).toBe('modules-page-hint-accounting');
      expect(spectator.query('#modules-page-hint-accounting')?.textContent).toContain(
        'Available soon'
      );
    });

    it('a tap asks for no reason and sends nothing', () => {
      spectator.click(switches()[0]);

      expect(ask).not.toHaveBeenCalled();
      expect(run).not.toHaveBeenCalled();
    });

    it('can still be turned OFF when it is already on (nothing locks an installation that has it on)', () => {
      data.set([moduleOf('accounting', true)]);
      spectator.detectChanges();

      expect(switches()[0].getAttribute('aria-disabled')).toBeNull();
      expect(text()).not.toContain('Available soon');
    });
  });

  describe('switching asks for a reason first', () => {
    beforeEach(() => {
      data.set([moduleOf('accounting', true), moduleOf('batch', false)]);
      spectator.detectChanges();
    });

    it('asks with a required reason of at least 5 characters, then sends enabled and the reason', () => {
      ask.mockReturnValue(of('Not used any more'));
      run.mockReturnValue(of(result('accounting', false, 0)));

      spectator.click(switches()[0]);

      const request = ask.mock.calls[0][0];
      expect(request.heading).toBe('Turn off Accounting?');
      expect(request.reasonRequired).toBe(true);
      expect(request.minReasonLength).toBe(MIN_REASON_LENGTH);
      expect(run).toHaveBeenCalledWith({
        code: 'accounting',
        enabled: false,
        reason: 'Not used any more'
      });
    });

    it('announces the result once, in one toast', () => {
      ask.mockReturnValue(of('Not used any more'));
      run.mockReturnValue(of(result('accounting', false, 0)));

      spectator.click(switches()[0]);

      expect(success).toHaveBeenCalledTimes(1);
      expect(success).toHaveBeenCalledWith('Accounting is now off.');
    });

    it('says how many roles regain their saved permissions when a module is turned on', () => {
      ask.mockReturnValue(of('Needed for lots'));
      run.mockReturnValue(of(result('batch', true, 3)));

      spectator.click(switches()[1]);

      expect(ask.mock.calls[0][0].heading).toBe('Turn on Module batch?');
      expect(success).toHaveBeenCalledWith(
        'Module batch is now on. 3 roles regain their saved permissions.'
      );
    });

    it('says it in the singular for one role, and nothing about roles for none', () => {
      ask.mockReturnValue(of('Needed for lots'));
      run.mockReturnValue(of(result('batch', true, 1)));
      spectator.click(switches()[1]);
      expect(success).toHaveBeenLastCalledWith(
        'Module batch is now on. 1 role regains its saved permissions.'
      );

      run.mockReturnValue(of(result('batch', true, 0)));
      spectator.click(switches()[1]);
      expect(success).toHaveBeenLastCalledWith('Module batch is now on.');
    });

    it('sends nothing when the person cancels the reason dialog', () => {
      ask.mockReturnValue(EMPTY);

      spectator.click(switches()[0]);

      expect(run).not.toHaveBeenCalled();
      expect(success).not.toHaveBeenCalled();
    });
  });

  describe('every failure of the switch is shown on the page, in words', () => {
    const fail = (status: number, message = 'API sentence') => {
      operation.set({ error: { status, message } });
      spectator.detectChanges();
    };

    it('400 shows the sentence the API gave (a reason that is too short, a missing flag)', () => {
      fail(HTTP_STATUS.BadRequest, 'Enter a reason of at least 5 characters.');

      expect(banner()).toContain('Enter a reason of at least 5 characters.');
      expect(load).toHaveBeenCalledTimes(1);
    });

    it('403 says the person may not, and does not guess the reason', () => {
      fail(HTTP_STATUS.Forbidden);

      expect(banner()).toContain('You are not allowed to turn modules on or off.');
    });

    it('404 says the module is gone and reloads the list', () => {
      fail(HTTP_STATUS.NotFound);

      expect(banner()).toContain('That module is not available any more. The list was refreshed.');
      expect(load).toHaveBeenCalledTimes(2);
    });

    it('409 says another change is in progress and reloads the list', () => {
      fail(HTTP_STATUS.Conflict);

      expect(banner()).toContain('Another change to the modules is in progress.');
      expect(load).toHaveBeenCalledTimes(2);
    });

    it('the banner goes away when the next switch starts', () => {
      data.set([moduleOf('accounting', true)]);
      fail(HTTP_STATUS.Forbidden);
      ask.mockReturnValue(EMPTY);

      spectator.click(switches()[0]);
      spectator.detectChanges();

      expect(spectator.query('app-banner')).toBeNull();
    });
  });

  it('forgets a failure left by an earlier visit: no banner and no extra reload when the page opens again', () => {
    operation.set({ error: { status: HTTP_STATUS.Conflict, message: 'Busy.' } });
    load.mockClear();

    spectator = createComponent();
    spectator.detectChanges();

    expect(reset).toHaveBeenCalled();
    expect(spectator.query('app-banner')).toBeNull();
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('links back to Settings only for a person who may open Settings', () => {
    expect(spectator.component.$backLink()).toBeUndefined();

    held.add(PERMISSION.SettingsManage);
    spectator = createComponent();
    spectator.detectChanges();
    expect(spectator.component.$backLink()).toEqual(['/settings']);
  });

  it('says so when there are no modules', () => {
    data.set([]);
    spectator.detectChanges();

    expect(text()).toContain('No modules are available.');
  });
});
