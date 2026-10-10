import { EnvironmentProviders, isDevMode, makeEnvironmentProviders } from '@angular/core';
import { provideServiceWorker } from '@angular/service-worker';

/**
 * Registers ngsw-worker.js (production builds only). PwaService.start() is called by the app's startup routine once the
 * runtime config is loaded, because it depends on AuthService and so on the HTTP clients.
 */
export function providePwa(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000'
    })
  ]);
}
