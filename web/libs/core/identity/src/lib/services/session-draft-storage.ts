import { DRAFT_STORAGE_PREFIX } from '../constants/identity.const';

/**
 * Unsent work kept across a session expiry, in sessionStorage (this tab only, gone when the tab closes), under the id of the
 * user it belongs to. Nothing here is a secret, but it is the user's own input: it is removed at an explicit sign-out and when a
 * different user signs in. Storage can throw (private mode, quota, blocked site data); then nothing is kept and nothing fails.
 */
const keyOf = (userId: string, key: string): string => `${DRAFT_STORAGE_PREFIX}${userId}.${key}`;

function draftKeys(): string[] {
  const keys: string[] = [];
  for (let index = 0; index < sessionStorage.length; index++) {
    const key = sessionStorage.key(index);
    if (key?.startsWith(DRAFT_STORAGE_PREFIX)) {
      keys.push(key);
    }
  }
  return keys;
}

/** True when the draft was stored. */
export function saveDraft(userId: string, key: string, value: unknown): boolean {
  try {
    sessionStorage.setItem(keyOf(userId, key), JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}

/** Reads and removes a draft: it is restored once. Undefined when there is none or it cannot be read. */
export function takeDraft<T>(userId: string, key: string): T | undefined {
  try {
    const stored = sessionStorage.getItem(keyOf(userId, key));
    sessionStorage.removeItem(keyOf(userId, key));
    return stored === null ? undefined : (JSON.parse(stored) as T);
  } catch {
    return undefined;
  }
}

export function hasDrafts(userId: string): boolean {
  try {
    return draftKeys().some(key => key.startsWith(keyOf(userId, '')));
  } catch {
    return false;
  }
}

/** Removes the drafts of everyone but `keepUserId` (all of them when it is undefined). */
export function clearDrafts(keepUserId?: string): void {
  try {
    const kept = keepUserId === undefined ? undefined : keyOf(keepUserId, '');
    for (const key of draftKeys()) {
      if (kept === undefined || !key.startsWith(kept)) {
        sessionStorage.removeItem(key);
      }
    }
  } catch {
    // Storage is unavailable: nothing was kept.
  }
}
