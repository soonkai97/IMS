import { signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { EMPTY, of } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from '@ims/core/identity';
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  IDLE_OPERATION,
  failureOperation
} from '@ims/models';
import { LoginPageComponent } from './login.component';

function error(kind: ApiError['kind'], status: number, detail: string): ApiError {
  return { isApiError: true, kind, status, title: '', detail, fieldErrors: {}, extensions: {} };
}

const USERNAME = 'input[autocomplete=username]';
const PASSWORD = 'input[autocomplete=current-password]';
const WRONG_CREDENTIALS = error(API_ERROR_KIND.Unauthorized, 401, 'Invalid credentials.');

describe('LoginPageComponent failure messages', () => {
  let spectator: Spectator<LoginPageComponent>;
  const operation = signal<ApiOperation>(IDLE_OPERATION);
  const running = signal(false);
  const login = {
    $operation: operation,
    $isRunning: running,
    run: jest.fn(() => EMPTY),
    reset: jest.fn(() => operation.set(IDLE_OPERATION))
  };
  const createComponent = createComponentFactory({
    component: LoginPageComponent,
    providers: [provideRouter([]), { provide: AuthService, useValue: { login } }]
  });

  const alerts = (): Element[] => spectator.queryAll('app-banner[role=alert]');
  const banner = (): string =>
    spectator.query('app-banner[role=alert]')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const fieldMessages = (): Element[] => spectator.queryAll('.ui-form-field-error');

  /** The page is open first; the failure arrives afterwards, like a real sign-in answer. */
  function show(failure: ApiError): void {
    operation.set(IDLE_OPERATION);
    running.set(false);
    login.run.mockClear();
    spectator = createComponent();
    spectator.detectChanges();
    operation.set(failureOperation(failure));
    spectator.detectChanges();
  }

  it('wrong credentials: the same sentence for every cause (E7)', () => {
    show(WRONG_CREDENTIALS);

    expect(banner()).toContain('Email, username or password is wrong.');
  });

  it('EA-06: an empty submit puts the focus on the first field with an error', async () => {
    operation.set(IDLE_OPERATION);
    spectator = createComponent();
    spectator.detectChanges();

    spectator.component.onSubmit();
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(document.activeElement?.getAttribute('autocomplete')).toBe('username');
    expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');
  });

  const rateLimited = (seconds?: number): ApiError => ({
    ...error(API_ERROR_KIND.Unknown, 429, 'Too many requests. The API sentence, in English.'),
    extensions: seconds === undefined ? {} : { retryAfterSeconds: seconds }
  });
  const locked = (minutes: number): ApiError => ({
    ...error(API_ERROR_KIND.Unauthorized, 401, 'The API sentence, in English.'),
    extensions: { code: 'account_locked', retryAfterMinutes: minutes }
  });

  describe('R25: the wait and the cause are written by the client, from the numbers the API sends (EA-24, EA-13)', () => {
    it('the rate limit names the wait in seconds, and in minutes (rounded up) from 90 seconds', () => {
      show(rateLimited(42));
      expect(banner()).toBe(
        'Too many sign-in attempts from this network. Try again in 42 seconds.'
      );

      show(rateLimited(1));
      expect(banner()).toContain('in 1 second.');

      show(rateLimited(120));
      expect(banner()).toBe('Too many sign-in attempts from this network. Try again in 2 minutes.');

      show(rateLimited());
      expect(banner()).toBe('Too many attempts. Wait a minute and try again.');
    });

    it('a locked account shows the wait, never the API sentence', () => {
      show(locked(14));
      expect(banner()).toBe('Too many failed attempts. Try again in 14 minutes.');

      show(locked(1));
      expect(banner()).toBe('Too many failed attempts. Try again in 1 minute.');
    });

    it('a network failure and a server failure each have their own sentence', () => {
      show(error(API_ERROR_KIND.Network, 0, ''));
      expect(banner()).toBe("Can't reach the server. Check your connection and try again.");

      show(error(API_ERROR_KIND.Server, 500, 'Internal detail'));
      expect(banner()).toBe('Sign-in is not working right now. Try again in a moment.');
      // An environment failure is a warning that is still the one alert, and there is no toast.
      expect(alerts()).toHaveLength(1);
      expect(spectator.query('app-banner.--warning')).not.toBeNull();
      expect(spectator.queryAll('app-toast')).toHaveLength(0);
    });

    it('a language change rewrites the banner and keeps what was typed', async () => {
      operation.set(IDLE_OPERATION);
      spectator = createComponent();
      spectator.detectChanges();
      const i18n = spectator.inject(I18nService);
      spectator.typeInElement('staff', USERNAME);
      operation.set(failureOperation(locked(15)));
      spectator.detectChanges();

      await i18n.setLanguage('zh');
      spectator.detectChanges();

      expect(banner()).toBe('失败次数过多。请 15 分钟后重试。');
      expect(spectator.query<HTMLInputElement>(USERNAME)?.value).toBe('staff');
      await i18n.setLanguage('en');
    });
  });

  describe('R25: motion that carries no meaning of its own', () => {
    const form = (): Element | null => spectator.query('form');

    it('a failed attempt shakes the form once; an edit takes the shake away so the next failure plays it again', () => {
      show(WRONG_CREDENTIALS);
      expect(form()?.classList.contains('--shake')).toBe(true);

      spectator.typeInElement('s', USERNAME);
      expect(form()?.classList.contains('--shake')).toBe(false);

      operation.set(failureOperation(WRONG_CREDENTIALS));
      spectator.detectChanges();
      expect(form()?.classList.contains('--shake')).toBe(true);
    });
  });

  describe('D3: the error banner never outlives the attempt that caused it', () => {
    it('a failed sign-in with both fields filled shows only the banner', () => {
      show(WRONG_CREDENTIALS);
      spectator.typeInElement('staff', USERNAME);
      spectator.typeInElement('pw', PASSWORD);
      spectator.component.onSubmit();
      operation.set(failureOperation(WRONG_CREDENTIALS));
      spectator.detectChanges();

      expect(alerts()).toHaveLength(1);
      expect(fieldMessages()).toHaveLength(0);
    });

    it('typing a character in the username removes the banner', () => {
      show(WRONG_CREDENTIALS);
      expect(alerts()).toHaveLength(1);

      spectator.typeInElement('s', USERNAME);

      expect(alerts()).toHaveLength(0);
    });

    it('typing a character in the password removes the banner', () => {
      show(WRONG_CREDENTIALS);

      spectator.typeInElement('x', PASSWORD);

      expect(alerts()).toHaveLength(0);
    });

    it('submitting both fields empty after a banner shows the two field messages and no banner', () => {
      show(WRONG_CREDENTIALS);
      expect(alerts()).toHaveLength(1);

      spectator.component.onSubmit();
      spectator.detectChanges();

      expect(alerts()).toHaveLength(0);
      // The spec copy says what to do, not "This field is required.".
      expect(fieldMessages().map(m => m.textContent?.trim())).toEqual([
        'Enter your email or username.',
        'Enter your password.'
      ]);
      expect(login.run).not.toHaveBeenCalled();
    });

    it('a page that is opened again starts without the previous failure (reload, navigation)', () => {
      operation.set(failureOperation(WRONG_CREDENTIALS));

      spectator = createComponent();
      spectator.detectChanges();

      expect(alerts()).toHaveLength(0);
      expect(fieldMessages()).toHaveLength(0);
    });

    it('keeps nothing about the failure in web storage or cookies', () => {
      const writes = jest.spyOn(Storage.prototype, 'setItem');
      show(WRONG_CREDENTIALS);
      spectator.typeInElement('s', USERNAME);
      spectator.component.onSubmit();
      spectator.detectChanges();

      expect(writes).not.toHaveBeenCalled();
      expect(document.cookie).toBe('');
      writes.mockRestore();
    });

    it('a second Enter while the request runs does not send it twice', () => {
      show(WRONG_CREDENTIALS);
      spectator.typeInElement('staff', USERNAME);
      spectator.typeInElement('pw', PASSWORD);
      running.set(true);

      spectator.component.onSubmit();

      expect(login.run).not.toHaveBeenCalled();
    });

    it('the session-expired notice is not an error and is not touched by typing', () => {
      operation.set(IDLE_OPERATION);
      spectator = createComponent({ props: { reason: 'expired' } as never });
      spectator.detectChanges();
      expect(spectator.queryAll('app-banner[role=status]')).toHaveLength(1);

      spectator.typeInElement('s', USERNAME);

      expect(spectator.queryAll('app-banner[role=status]')).toHaveLength(1);
    });
  });

  describe('R25: the password and the page after an attempt', () => {
    beforeEach(() => {
      operation.set(IDLE_OPERATION);
      running.set(false);
      login.run.mockClear();
      spectator = createComponent();
      spectator.detectChanges();
    });
    const password = (): HTMLInputElement =>
      spectator.query<HTMLInputElement>(PASSWORD) as HTMLInputElement;

    it('stays busy until the next page has opened, so a second Enter signs in nothing twice', () => {
      jest
        .spyOn(spectator.inject(Router), 'navigateByUrl')
        .mockReturnValue(new Promise(() => undefined));
      login.run.mockReturnValueOnce(of({}) as never);
      spectator.typeInElement('staff', USERNAME);
      spectator.typeInElement('pw', PASSWORD);

      spectator.component.onSubmit();
      spectator.detectChanges();
      spectator.component.onSubmit();

      expect(spectator.component.$busy()).toBe(true);
      expect(spectator.query<HTMLInputElement>(USERNAME)?.readOnly).toBe(true);
      expect(login.run).toHaveBeenCalledTimes(1);
    });

    it('after a failed attempt the password has the focus, its text selected, whatever had it before', async () => {
      spectator.typeInElement('wrong', PASSWORD);
      spectator.query<HTMLElement>('app-button button')?.focus();
      operation.set(failureOperation(WRONG_CREDENTIALS));
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(document.activeElement).toBe(password());
      expect([password().selectionStart, password().selectionEnd]).toEqual([0, 'wrong'.length]);
    });

    it('a revealed password is hidden again after a submit and when the page is hidden', () => {
      const reveal = (): void => spectator.click('.ui-input-toggle');
      spectator.typeInElement('staff', USERNAME);
      spectator.typeInElement('pw', PASSWORD);

      reveal();
      expect(password().type).toBe('text');
      spectator.component.onSubmit();
      spectator.detectChanges();
      expect(password().type).toBe('password');

      reveal();
      jest.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
      document.dispatchEvent(new Event('visibilitychange'));
      spectator.detectChanges();
      expect(password().type).toBe('password');
    });
  });

  describe('D4: required marker and legend', () => {
    beforeEach(() => {
      operation.set(IDLE_OPERATION);
      running.set(false);
      spectator = createComponent();
      spectator.detectChanges();
    });

    it('shows the legend once and no "(required)" text', () => {
      expect(spectator.queryAll('app-form-legend')).toHaveLength(1);
      expect(spectator.element.textContent).not.toContain('(required)');
    });

    it('both fields carry a hidden-star asterisk and the required state, and the name has no "required" in it', () => {
      expect(spectator.queryAll('.ui-form-field-asterisk')).toHaveLength(2);
      expect(spectator.query(USERNAME)?.hasAttribute('required')).toBe(true);
      expect(spectator.query(PASSWORD)?.hasAttribute('required')).toBe(true);
      // The name is the label alone: a screen reader says "required" from the state, not twice (review of R02).
      const names = spectator.queryAll('form app-form-field label').map(label => {
        const copy = label.cloneNode(true) as Element;
        copy.querySelectorAll('[aria-hidden="true"]').forEach(hidden => hidden.remove());
        return (copy.textContent ?? '').replace(/\s+/g, ' ').trim();
      });
      expect(names).toEqual(['Email or username', 'Password']);
    });
  });

  describe('while the request runs', () => {
    beforeEach(() => {
      operation.set(IDLE_OPERATION);
      running.set(false);
      spectator = createComponent();
      spectator.detectChanges();
    });

    it('both fields are read-only (not disabled) so nothing can be edited mid-request', () => {
      running.set(true);
      spectator.detectChanges();

      for (const selector of [USERNAME, PASSWORD]) {
        const input = spectator.query<HTMLInputElement>(selector);
        expect(input?.readOnly).toBe(true);
        expect(input?.disabled).toBe(false);
      }
    });

    it('an edit that still arrives (late autofill) does not reset the running sign-in', () => {
      running.set(true);
      login.reset.mockClear();

      spectator.typeInElement('late', USERNAME);

      expect(login.reset).not.toHaveBeenCalled();
    });
  });
});
