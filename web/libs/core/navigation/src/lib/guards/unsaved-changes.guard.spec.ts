import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterStateSnapshot } from '@angular/router';
import { EMPTY, Observable, firstValueFrom, of } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from '@ims/core/identity';
import { ConfirmService } from '@ims/core/notification';
import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  const expired = signal(false);
  const ask = jest.fn();

  beforeEach(() => {
    expired.set(false);
    ask.mockReset().mockReturnValue(EMPTY);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { $sessionExpired: expired.asReadonly() } },
        { provide: ConfirmService, useValue: { ask } },
        { provide: I18nService, useValue: { t: (key: string) => key } }
      ]
    });
  });

  const run = (dirty: boolean) =>
    TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(
        { hasUnsavedChanges: () => dirty } satisfies HasUnsavedChanges,
        undefined as never,
        undefined as never,
        {} as RouterStateSnapshot
      )
    );

  it('lets a clean page go without asking', () => {
    expect(run(false)).toBe(true);
    expect(ask).not.toHaveBeenCalled();
  });

  it('asks "Discard changes?" when a page with unsaved edits is left, and stays on "Keep editing"', async () => {
    const answer = run(true) as Observable<boolean>;

    expect(ask).toHaveBeenCalledTimes(1);
    expect(ask.mock.calls[0][0].heading).toBe('dialog.discard.title');
    await expect(firstValueFrom(answer)).resolves.toBe(false);
  });

  it('leaves on "Discard"', async () => {
    ask.mockReturnValue(of(undefined));

    await expect(firstValueFrom(run(true) as Observable<boolean>)).resolves.toBe(true);
  });

  it('does not ask once the session has expired: the page cannot save, so the question only traps the user', () => {
    expired.set(true);

    expect(run(true)).toBe(true);
    expect(ask).not.toHaveBeenCalled();
  });
});
