import { EnvironmentProviders, Provider, inject, makeEnvironmentProviders } from '@angular/core';
import { API_BASE_URL } from '@ims/api';
import { AppConfig } from './app-config';

/**
 * Feeds the base URL to the generated NSwag clients. AppConfig.load() must have finished before the first client is
 * created: the app's startup initializer awaits it first.
 */
export function provideApi(): EnvironmentProviders {
  const baseUrl: Provider = {
    provide: API_BASE_URL,
    useFactory: () => inject(AppConfig).$apiBaseUrl()
  };
  return makeEnvironmentProviders([baseUrl]);
}
