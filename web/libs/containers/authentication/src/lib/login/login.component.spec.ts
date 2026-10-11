import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { EMPTY } from 'rxjs';
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

  it('EA-24: the rate limit shows the API sentence with the wait', () => {
    show(error(API_ERROR_KIND.Unknown, 429, 'Too many requests. Try again in 12 seconds.'));

    expect(banner()).toContain('Too many requests. Try again in 12 seconds.');
  });

  it('EA-24: a rate limit without a sentence gets ours', () => {
    show(error(API_ERROR_KIND.Unknown, 429, ''));

    expect(banner()).toContain('Too many attempts. Wait a minute and try again.');
  });

  it('EA-13: a locked account shows the E7 sentence with the minutes, not the generic one', () => {
    show({
      ...error(
        API_ERROR_KIND.Unauthorized,
        401,
        'Too many failed attempts. Try again in 14 minutes.'
      ),
      extensions: { code: 'account_locked', retryAfterMinutes: 14 }
    });

    expect(banner()).toContain('Too many failed attempts. Try again in 14 minutes.');
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
      expect(fieldMessages()).toHaveLength(2);
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
      const names = spectator.queryAll('app-form-field label').map(label => {
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
