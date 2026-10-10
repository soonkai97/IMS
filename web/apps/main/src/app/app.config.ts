import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import {
  ApplicationConfig,
  Injector,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  runInInjectionContext
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  AppConfig,
  correlationInterceptor,
  errorInterceptor,
  loadingInterceptor,
  provideApi,
  timeoutInterceptor
} from '@ims/core/http';
import { I18nService, provideUiText } from '@ims/core/i18n';
import { AuthService, authInterceptor } from '@ims/core/identity';
import { NAV_BADGE_SOURCES } from '@ims/core/navigation';
import { PwaService, providePwa } from '@ims/core/pwa';
import { ApprovalsStore } from '@ims/features/approvals';
import { StockStore } from '@ims/features/stock';
import { APP_ROUTES } from './app.routes';

/**
 * Startup order matters: runtime config (API URL) first, then the language, then try to restore the session from a
 * stored refresh token. The first screen renders only after all three finished.
 */
function startUp(): Promise<void> {
  // inject() only works before the first await, so grab the injector now. AuthService (and so the HTTP clients) must
  // be created AFTER the runtime config is loaded, otherwise they would freeze the default API URL.
  const injector = inject(Injector);
  return (async () => {
    await injector.get(AppConfig).load();
    await injector.get(I18nService).init();
    await firstValueFrom(injector.get(AuthService).restoreSession());
    runInInjectionContext(injector, () => injector.get(PwaService).start());
  })();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(APP_ROUTES, withComponentInputBinding()),
    // Order = request order. Error mapping is first so it sees failures after the auth interceptor tried to recover.
    provideHttpClient(
      withXhr(),
      withInterceptors([
        errorInterceptor,
        correlationInterceptor,
        loadingInterceptor,
        timeoutInterceptor,
        authInterceptor
      ])
    ),
    provideApi(),
    provideUiText(),
    providePwa(),
    provideAppInitializer(startUp),
    {
      provide: NAV_BADGE_SOURCES,
      useFactory: () => [
        { menuId: 'approvals', $count: inject(ApprovalsStore).$waitingCount },
        { menuId: 'low-stock', $count: inject(StockStore).$lowStockCount }
      ]
    }
  ]
};
