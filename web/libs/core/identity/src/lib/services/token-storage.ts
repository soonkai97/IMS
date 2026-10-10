import { Injectable } from '@angular/core';
import { REFRESH_TOKEN_STORAGE_KEY } from '../constants/identity.const';

/**
 * Keeps the refresh token across reloads. The short-lived access token is held in memory only (AuthService).
 * Storage can throw (private mode, blocked site data), so every access is guarded.
 */
@Injectable({ providedIn: 'root' })
export class TokenStorage {
  readRefreshToken(): string | undefined {
    try {
      return localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY) ?? undefined;
    } catch {
      return undefined;
    }
  }

  writeRefreshToken(token: string): void {
    try {
      localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, token);
    } catch {
      // Without storage the session lasts until the tab closes.
    }
  }

  clear(): void {
    try {
      localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
    } catch {
      // Nothing to clear.
    }
  }
}
