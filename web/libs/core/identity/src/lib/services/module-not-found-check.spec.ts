import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { ModuleDto } from '@ims/api';
import { API_NOT_FOUND_CHECK, ApiNotFoundCheck } from '@ims/core/http';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from './auth.service';
import { provideModuleNotFoundCheck } from './module-not-found-check';
import { ModulesStore } from './modules.store';

const accounting: ModuleDto = {
  code: 'accounting',
  name: 'Accounting',
  description: '',
  enabled: false,
  requires: []
};

describe('provideModuleNotFoundCheck', () => {
  const signedIn = { value: true };
  const checkAfterNotFound = jest.fn();
  let check: ApiNotFoundCheck;

  beforeEach(() => {
    signedIn.value = true;
    checkAfterNotFound.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideModuleNotFoundCheck(),
        { provide: AuthService, useValue: { $isAuthenticated: () => signedIn.value } },
        { provide: ModulesStore, useValue: { checkAfterNotFound } }
      ]
    });
    check = TestBed.inject(API_NOT_FOUND_CHECK) as ApiNotFoundCheck;
  });

  it('says in words which feature was turned off', async () => {
    checkAfterNotFound.mockReturnValue(of([accounting]));

    const sentence = await firstValueFrom(check('http://api.test/api/products'));

    expect(sentence).toBe('This feature was turned off: Accounting. The menu was updated.');
  });

  it('names every module that was turned off, in the user language', async () => {
    await TestBed.inject(I18nService).setLanguage('ms');
    checkAfterNotFound.mockReturnValue(
      of([accounting, { ...accounting, code: 'batch', name: 'Batch' }])
    );

    const sentence = await firstValueFrom(check('http://api.test/api/products'));

    expect(sentence).toBe('Ciri ini telah dimatikan: Perakaunan, Batch. Menu telah dikemas kini.');
  });

  it('says nothing when no module was turned off, so the ordinary not-found sentence stays', async () => {
    checkAfterNotFound.mockReturnValue(of([]));

    expect(await firstValueFrom(check('http://api.test/api/products'))).toBeUndefined();
  });

  it.each([
    'http://api.test/api/modules',
    'http://api.test/api/modules/accounting',
    'http://api.test/api/auth/me'
  ])('never checks after a 404 of %s (a loop, or not about modules)', async url => {
    expect(await firstValueFrom(check(url))).toBeUndefined();
    expect(checkAfterNotFound).not.toHaveBeenCalled();
  });

  it('does not check for a visitor who is not signed in', async () => {
    signedIn.value = false;

    expect(await firstValueFrom(check('http://api.test/api/products'))).toBeUndefined();
    expect(checkAfterNotFound).not.toHaveBeenCalled();
  });
});
