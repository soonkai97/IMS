import { LEGACY_REFRESH_TOKEN_STORAGE_KEY } from '../constants/identity.const';

/**
 * Earlier releases kept the 7-day refresh token in localStorage, readable by any script on the page. It now lives in an
 * HttpOnly cookie, so a leftover entry is deleted once at start-up. Storage can throw (private mode, blocked site
 * data); then there is nothing to delete.
 */
export function removeLegacyRefreshToken(): void {
  try {
    localStorage.removeItem(LEGACY_REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // Storage is unavailable: nothing was stored.
  }
}
