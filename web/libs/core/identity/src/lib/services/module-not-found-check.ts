import { Provider, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';
import { API_NOT_FOUND_CHECK } from '@ims/core/http';
import { I18nService } from '@ims/core/i18n';
import { moduleName } from '../rules/module-text';
import { AuthService } from './auth.service';
import { ModulesStore } from './modules.store';

/** The calls that never mean "a module was turned off": the module list itself (a loop) and sign-in, refresh and sign-out. */
const NOT_ABOUT_MODULES = ['/api/modules', '/api/auth/'] as const;

/**
 * Wires the 404 check of the error interceptor to the modules: a stale screen calls an endpoint of a module that was turned off, the API
 * answers like an unknown address, and the person is told in words what happened (and the menu already follows) instead of "not found".
 */
export function provideModuleNotFoundCheck(): Provider {
  return {
    provide: API_NOT_FOUND_CHECK,
    useFactory: () => {
      const auth = inject(AuthService);
      const modules = inject(ModulesStore);
      const i18n = inject(I18nService);
      return (requestUrl: string): Observable<string | undefined> =>
        !auth.$isAuthenticated() || NOT_ABOUT_MODULES.some(part => requestUrl.includes(part))
          ? of(undefined)
          : modules.checkAfterNotFound().pipe(
              map(turnedOff =>
                turnedOff.length === 0
                  ? undefined
                  : i18n.t('modules.turnedOff', {
                      names: turnedOff.map(module => moduleName(i18n, module)).join(', ')
                    })
              )
            );
    }
  };
}
