import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
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

describe('LoginPageComponent failure messages', () => {
  let spectator: Spectator<LoginPageComponent>;
  const operation = signal<ApiOperation>(IDLE_OPERATION);
  const createComponent = createComponentFactory({
    component: LoginPageComponent,
    providers: [
      provideRouter([]),
      {
        provide: AuthService,
        useValue: { login: { $operation: operation, $isRunning: signal(false), run: jest.fn() } }
      }
    ]
  });

  const banner = (): string =>
    spectator.query('app-banner[role=alert]')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  function show(failure: ApiError): void {
    operation.set(failureOperation(failure));
    spectator = createComponent();
    spectator.detectChanges();
  }

  it('wrong credentials: the same sentence for every cause (E7)', () => {
    show(error(API_ERROR_KIND.Unauthorized, 401, 'Invalid credentials.'));

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

  it('EA-24: the rate limit says to wait, not "could not be processed"', () => {
    show(error(API_ERROR_KIND.Unknown, 429, 'The request could not be processed.'));

    expect(banner()).toContain('Too many attempts. Wait a minute and try again.');
  });
});
