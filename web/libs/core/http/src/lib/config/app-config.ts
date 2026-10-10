import { Injectable, signal } from '@angular/core';
import { environment } from './environment';

const CONFIG_URL = 'assets/config.json';

/**
 * Runtime configuration. The build ships defaults (environment.ts); a deployment can override them without
 * rebuilding by editing assets/config.json, e.g. { "apiBaseUrl": "https://ims.example.com" }.
 */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  /** Origin of the IMS API without a trailing slash; empty = same origin as the web app. */
  readonly $apiBaseUrl = signal(environment.apiBaseUrl);

  async load(): Promise<void> {
    try {
      const response = await fetch(CONFIG_URL, { cache: 'no-store' });
      if (!response.ok) {
        return;
      }
      const config: unknown = await response.json();
      if (isConfigFile(config)) {
        this.$apiBaseUrl.set(config.apiBaseUrl.replace(/\/+$/, ''));
      }
    } catch {
      // Missing or invalid config.json: keep the build defaults.
    }
  }
}

function isConfigFile(value: unknown): value is { apiBaseUrl: string } {
  return (
    typeof value === 'object' &&
    value !== null &&
    'apiBaseUrl' in value &&
    typeof value.apiBaseUrl === 'string'
  );
}
