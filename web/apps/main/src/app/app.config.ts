import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import {
  ApplicationConfig,
  Injector,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  runInInjectionContext
} from '@angular/core';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
  withViewTransitions
} from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  AppConfig,
  correlationInterceptor,
  credentialsInterceptor,
  csrfHeaderInterceptor,
  errorInterceptor,
  ifMatchInterceptor,
  loadingInterceptor,
  provideApi,
  timeoutInterceptor
} from '@ims/core/http';
import { I18nService, provideUiText } from '@ims/core/i18n';
import { AuthService, authInterceptor, provideModuleNotFoundCheck } from '@ims/core/identity';
import {
  NAV_BADGE_SOURCES,
  PageTitleStrategy,
  ROUTE_FADE,
  ROUTE_SCROLLING
} from '@ims/core/navigation';
import { provideDialogFeedback } from '@ims/core/notification';
import { PwaService, providePwa } from '@ims/core/pwa';
import { ApprovalsStore } from '@ims/features/approvals';
import { StockStore } from '@ims/features/stock';
import { APP_ROUTES } from './app.routes';

/**
 * Startup order matters: runtime config (API URL) first, then the language, then try to restore the session with one
 * refresh call (the HttpOnly refresh cookie; 401 = signed out). The first screen renders only after all three finished.
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
    provideRouter(
      APP_ROUTES,
      withComponentInputBinding(),
      withInMemoryScrolling(ROUTE_SCROLLING),
      withViewTransitions(ROUTE_FADE)
    ),
    { provide: TitleStrategy, useExisting: PageTitleStrategy },
    // Order = request order. Error mapping is first so it sees failures after the auth interceptor tried to recover.
    provideHttpClient(
      withXhr(),
      withInterceptors([
        errorInterceptor,
        correlationInterceptor,
        credentialsInterceptor,
        csrfHeaderInterceptor,
        ifMatchInterceptor,
        loadingInterceptor,
        timeoutInterceptor,
        authInterceptor
      ])
    ),
    provideApi(),
    provideModuleNotFoundCheck(),
    provideUiText(),
    provideDialogFeedback(),
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
