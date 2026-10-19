import { DRAFT_MAX_CHARS, DRAFT_STORAGE_PREFIX, DRAFT_TTL_MS } from '../constants/identity.const';

/**
 * Unsent work kept across a session expiry, in sessionStorage (this tab only), under the id of the user it belongs to, with the
 * time it was saved: a draft older than DRAFT_TTL_MS is gone (a reopened tab or a restored browser session must not bring back
 * business data hours later), and one larger than DRAFT_MAX_CHARS is not kept. It is removed at an explicit sign-out and when a
 * different user signs in. Nothing here is a secret, but it is the user's own input. Storage can throw (private mode, quota,
 * blocked site data); then nothing is kept and nothing fails.
 */
interface StoredDraft {
  readonly savedAt: number;
  readonly value: unknown;
}

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

/** The stored entry, or undefined when it is too large, unreadable, not in the expected form or older than the TTL. */
function readFresh(stored: string | null): StoredDraft | undefined {
  if (stored === null || stored.length > DRAFT_MAX_CHARS) {
    return undefined;
  }
  try {
    const entry = JSON.parse(stored) as Partial<StoredDraft> | null;
    const age = typeof entry?.savedAt === 'number' ? Date.now() - entry.savedAt : Infinity;
    return entry !== null && age >= 0 && age <= DRAFT_TTL_MS && 'value' in entry
      ? (entry as StoredDraft)
      : undefined;
  } catch {
    return undefined;
  }
}

/** True when the draft was stored. */
export function saveDraft(userId: string, key: string, value: unknown): boolean {
  try {
    const stored = JSON.stringify({ savedAt: Date.now(), value } satisfies StoredDraft);
    if (stored.length > DRAFT_MAX_CHARS) {
      return false;
    }
    sessionStorage.setItem(keyOf(userId, key), stored);
    return true;
  } catch {
    return false;
  }
}

/** Reads and removes a draft: it is restored once. Undefined when there is none, it is too old or it cannot be read. */
export function takeDraft<T>(userId: string, key: string): T | undefined {
  try {
    const stored = sessionStorage.getItem(keyOf(userId, key));
    sessionStorage.removeItem(keyOf(userId, key));
    return readFresh(stored)?.value as T | undefined;
  } catch {
    return undefined;
  }
}

/** True when this user has a draft that is still fresh. */
export function hasDrafts(userId: string): boolean {
  try {
    const prefix = keyOf(userId, '');
    return draftKeys().some(
      key => key.startsWith(prefix) && readFresh(sessionStorage.getItem(key)) !== undefined
    );
  } catch {
    return false;
  }
}

/**
 * Removes the drafts of everyone but `keepUserId` (all of them when it is undefined), and also any entry, of whoever, that can no
 * longer be read: expired, too large or not in the stored form.
 */
export function clearDrafts(keepUserId?: string): void {
  try {
    const kept = keepUserId === undefined ? undefined : keyOf(keepUserId, '');
    for (const key of draftKeys()) {
      if (
        kept === undefined ||
        !key.startsWith(kept) ||
        readFresh(sessionStorage.getItem(key)) === undefined
      ) {
        sessionStorage.removeItem(key);
      }
    }
  } catch {
    // Storage is unavailable: nothing was kept.
  }
}
