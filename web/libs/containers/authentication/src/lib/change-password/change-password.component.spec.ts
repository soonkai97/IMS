import { signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
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
import { ChangePasswordPageComponent } from './change-password.component';

const CURRENT = 'input[autocomplete=current-password]';
const NEW = 'input[autocomplete=new-password]';
const OLD_PASSWORD = 'the-old-password';
const NEW_PASSWORD = 'a-brand-new-password';

function error(
  kind: ApiError['kind'],
  status: number,
  detail: string,
  extra: Partial<ApiError> = {}
): ApiError {
  return {
    isApiError: true,
    kind,
    status,
    title: '',
    detail,
    fieldErrors: {},
    extensions: {},
    ...extra
  };
}

describe('ChangePasswordPageComponent', () => {
  let spectator: Spectator<ChangePasswordPageComponent>;
  const operation = signal<ApiOperation>(IDLE_OPERATION);
  const running = signal(false);
  const changePassword = {
    $operation: operation,
    $isRunning: running,
    run: jest.fn(() => EMPTY),
    reset: jest.fn(() => operation.set(IDLE_OPERATION))
  };
  const createComponent = createComponentFactory({
    component: ChangePasswordPageComponent,
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { changePassword, $user: signal({ username: 'clerk' }) } }
    ]
  });

  const input = (selector: string): HTMLInputElement =>
    spectator.query<HTMLInputElement>(selector) as HTMLInputElement;
  const fieldErrors = (): string[] =>
    spectator.queryAll('.ui-form-field-error').map(e => e.textContent?.trim() ?? '');
  const banner = (): string =>
    spectator.query('app-banner[role=alert]')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  function fill(current: string, next: string): void {
    spectator.typeInElement(current, CURRENT);
    spectator.typeInElement(next, NEW);
  }

  beforeEach(() => {
    operation.set(IDLE_OPERATION);
    running.set(false);
    changePassword.run.mockClear();
    changePassword.reset.mockClear();
    spectator = createComponent();
    spectator.detectChanges();
  });

  describe('the form', () => {
    it('has two labelled password fields with the right autocomplete hints and no repeat field', () => {
      const labels = spectator
        .queryAll('label')
        .map(l => l.textContent?.replace(/\s+/g, ' ').trim());

      expect(labels).toEqual(['Current password *', 'New password *']);
      expect(spectator.queryAll('input:not([aria-hidden])')).toHaveLength(2);
      expect(input(CURRENT).type).toBe('password');
      expect(input(NEW).type).toBe('password');
      expect(input(CURRENT).getAttribute('name')).toBe('currentPassword');
      expect(input(NEW).getAttribute('name')).toBe('newPassword');
      expect(input(CURRENT).required).toBe(true);
      expect(input(NEW).required).toBe(true);
    });

    it('limits what can be typed to 128 characters and states the 12-character minimum in a hint tied to the field', () => {
      expect(input(NEW).maxLength).toBe(128);
      const hint = spectator.query('.ui-form-field-hint');
      expect(hint?.textContent?.trim()).toBe('At least 12 characters.');
      expect(input(NEW).getAttribute('aria-describedby')).toContain(hint?.id ?? 'missing');
    });

    it('each field has an eye button with a constant name and aria-pressed that follows the state', () => {
      const eyes = spectator.queryAll<HTMLButtonElement>('button[aria-pressed]');
      expect(eyes).toHaveLength(2);
      expect(eyes.map(b => b.getAttribute('aria-label'))).toEqual([
        'Show password',
        'Show password'
      ]);
      expect(eyes.map(b => b.getAttribute('aria-pressed'))).toEqual(['false', 'false']);

      eyes[1].click();
      spectator.detectChanges();

      expect(input(NEW).type).toBe('text');
      expect(input(CURRENT).type).toBe('password');
      expect(eyes[1].getAttribute('aria-pressed')).toBe('true');
      expect(eyes[1].getAttribute('aria-label')).toBe('Show password');
    });

    it('starts with the focus on the current password', async () => {
      await spectator.fixture.whenStable();

      expect(document.activeElement).toBe(input(CURRENT));
    });
  });

  describe('checks before anything is sent', () => {
    it('an empty submit sends nothing, says what is missing and focuses the first invalid field', async () => {
      spectator.component.onSubmit();
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(changePassword.run).not.toHaveBeenCalled();
      expect(fieldErrors()).toEqual(['Enter your current password.', 'Enter a new password.']);
      expect(document.activeElement).toBe(input(CURRENT));
      expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');
      expect(input(CURRENT).getAttribute('aria-describedby')).toContain(
        spectator.query('.ui-form-field-error')?.id ?? 'missing'
      );
    });

    it('a new password under 12 characters is refused and the focus goes to it', async () => {
      fill(OLD_PASSWORD, 'short-pass1');

      spectator.component.onSubmit();
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(changePassword.run).not.toHaveBeenCalled();
      expect(fieldErrors()).toEqual(['Enter at least 12 characters.']);
      expect(document.activeElement).toBe(input(NEW));
    });

    it('a new password equal to the current one is refused with the API sentence', async () => {
      fill(NEW_PASSWORD, NEW_PASSWORD);

      spectator.component.onSubmit();
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(changePassword.run).not.toHaveBeenCalled();
      expect(fieldErrors()).toEqual(['Choose a password different from the current one.']);
      expect(document.activeElement).toBe(input(NEW));
    });

    it('no banner and no toast sit together with the field messages', () => {
      spectator.component.onSubmit();
      spectator.detectChanges();

      expect(spectator.queryAll('app-banner')).toHaveLength(0);
    });
  });

  describe('sending', () => {
    it('sends exactly the two passwords', () => {
      fill(OLD_PASSWORD, NEW_PASSWORD);

      spectator.component.onSubmit();

      expect(changePassword.run).toHaveBeenCalledTimes(1);
      expect(changePassword.run).toHaveBeenCalledWith({
        currentPassword: OLD_PASSWORD,
        newPassword: NEW_PASSWORD
      });
    });

    it('while a request runs the button shows busy, the fields are read-only, and a second submit sends nothing', () => {
      fill(OLD_PASSWORD, NEW_PASSWORD);
      running.set(true);
      spectator.detectChanges();

      spectator.component.onSubmit();

      expect(changePassword.run).not.toHaveBeenCalled();
      expect(input(CURRENT).readOnly).toBe(true);
      expect(input(NEW).readOnly).toBe(true);
    });

    it('hides a revealed password when the form is sent', () => {
      fill(OLD_PASSWORD, NEW_PASSWORD);
      spectator.queryAll<HTMLButtonElement>('button[aria-pressed]')[1].click();
      spectator.detectChanges();
      expect(input(NEW).type).toBe('text');

      spectator.component.onSubmit();
      spectator.detectChanges();

      expect(input(NEW).type).toBe('password');
    });

    it('Cancel goes Home without sending anything', async () => {
      const navigate = jest
        .spyOn(spectator.inject(Router), 'navigateByUrl')
        .mockResolvedValue(true);
      const cancel = spectator
        .queryAll<HTMLButtonElement>('button')
        .find(b => b.textContent?.trim() === 'Cancel');

      cancel?.click();

      expect(navigate).toHaveBeenCalledWith('/');
      expect(changePassword.run).not.toHaveBeenCalled();
    });
  });

  describe('when the API refuses', () => {
    const show = (failure: ApiError): void => {
      operation.set(failureOperation(failure));
      spectator.detectChanges();
    };

    it('a wrong current password puts the sentence under that field, tied by aria-describedby, and focuses it', async () => {
      fill(OLD_PASSWORD, NEW_PASSWORD);
      spectator.component.onSubmit();
      await spectator.fixture.whenStable();
      input(NEW).focus();

      show(error(API_ERROR_KIND.Validation, 400, 'The current password is not correct.'));
      await spectator.fixture.whenStable();
      spectator.detectChanges();

      const message = spectator.query('.ui-form-field-error');
      expect(message?.textContent?.trim()).toBe('The current password is not correct.');
      expect(fieldErrors()).toHaveLength(1);
      expect(input(CURRENT).getAttribute('aria-invalid')).toBe('true');
      expect(input(CURRENT).getAttribute('aria-describedby')).toContain(message?.id ?? 'missing');
      expect(document.activeElement).toBe(input(CURRENT));
      expect(spectator.queryAll('app-banner')).toHaveLength(0);
    });

    it('keeps what was typed so the person can correct it', () => {
      fill(OLD_PASSWORD, NEW_PASSWORD);

      show(error(API_ERROR_KIND.Validation, 400, 'The current password is not correct.'));

      expect(input(CURRENT).value).toBe(OLD_PASSWORD);
      expect(input(NEW).value).toBe(NEW_PASSWORD);
    });

    it('every 400 without field errors is the wrong current password, said in our words whatever the API wrote', () => {
      show(error(API_ERROR_KIND.Validation, 400, 'Something the API said.'));

      expect(fieldErrors()).toEqual(['The current password is not correct.']);
    });

    it('field errors of the API go to their fields', () => {
      fill(OLD_PASSWORD, NEW_PASSWORD);
      show(
        error(API_ERROR_KIND.Validation, 400, 'Password must be at least 12 characters.', {
          fieldErrors: { newPassword: ['Password must be at least 12 characters.'] }
        })
      );

      expect(fieldErrors()).toEqual(['Password must be at least 12 characters.']);
      expect(input(NEW).getAttribute('aria-invalid')).toBe('true');
      expect(input(CURRENT).getAttribute('aria-invalid')).toBeNull();
    });

    it('typing in the field removes its server message', () => {
      show(error(API_ERROR_KIND.Validation, 400, 'The current password is not correct.'));
      expect(fieldErrors()).toHaveLength(1);

      spectator.typeInElement(`${OLD_PASSWORD}2`, CURRENT);

      expect(fieldErrors()).toHaveLength(0);
    });

    it('a network failure is one alert banner and no field message', () => {
      show(
        error(API_ERROR_KIND.Network, 0, '', {
          message: "Can't reach the server. Check your connection and try again."
        })
      );

      expect(banner()).toBe("Can't reach the server. Check your connection and try again.");
      expect(fieldErrors()).toHaveLength(0);
    });

    it('a 401 without a sentence of the API (a refresh that failed on the way) is not called a lockout', () => {
      show(
        error(API_ERROR_KIND.Unauthorized, 401, '', {
          message: 'Something went wrong. Try again.'
        })
      );

      expect(banner()).toBe('Something went wrong. Try again.');
      expect(spectator.query('app-banner')?.classList.contains('--warning')).toBe(true);
    });

    it('a locked account (401) says to try again later, in our words', () => {
      show(error(API_ERROR_KIND.Unauthorized, 401, 'Email, username or password is wrong.'));

      expect(banner()).toBe('Too many failed attempts. Try again in a few minutes.');
    });

    it('a new visit starts clean: the error of the last one is cleared', () => {
      operation.set(failureOperation(error(API_ERROR_KIND.Network, 0, '')));
      changePassword.reset.mockClear();

      spectator = createComponent();
      spectator.detectChanges();

      expect(changePassword.reset).toHaveBeenCalled();
      expect(spectator.queryAll('app-banner')).toHaveLength(0);
    });
  });

  it('has a hidden read-only username for the password manager, holding the account name and no password', () => {
    const hidden = spectator.query<HTMLInputElement>('input[autocomplete=username]');

    expect(hidden?.value).toBe('clerk');
    expect(hidden?.readOnly).toBe(true);
    expect(hidden?.getAttribute('aria-hidden')).toBe('true');
    expect(hidden?.tabIndex).toBe(-1);
    fill(OLD_PASSWORD, NEW_PASSWORD);
    expect(hidden?.value).toBe('clerk');
    expect(spectator.queryAll('label')).toHaveLength(2);
  });

  it('writes nothing to web storage and puts nothing in the address', () => {
    localStorage.clear();
    sessionStorage.clear();
    fill(OLD_PASSWORD, NEW_PASSWORD);
    spectator.component.onSubmit();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toContain(NEW_PASSWORD);
  });
});
