/**
 * Signed-in sessions kept between scenarios, so a run does not sign in through the form for every scenario (the API allows
 * 20 sign-ins a minute per address). What is kept is the refresh cookie of a role, nothing else: a new browser context given
 * that cookie opens the app, the app refreshes its session (a separate, larger limit) and the user is in.
 *
 * The API rotates the refresh token on every use, so an entry is single-use: `takeSession` removes it, and `keepSession`
 * puts the newest cookie back when the scenario is done. A scenario that signs out keeps nothing. Anything missing,
 * expired or revoked (a new database, a restarted API) is not an error: the caller signs in through the form instead.
 *
 * The file lives in e2e/.cache (git-ignored), next to the limiter log, so a restarted worker and the next run find it.
 */
import type { BrowserContext } from '@playwright/test';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { API_URL, type Role } from './config';

type Cookie = Awaited<ReturnType<BrowserContext['cookies']>>[number];

export const CACHE_DIR = join(__dirname, '..', '.cache');
const slug = Buffer.from(API_URL).toString('hex').slice(0, 24);
const file = join(CACHE_DIR, `sessions-${slug}.json`);
/** The refresh cookie is HttpOnly and named `ims_refresh` (`__Secure-` prefixed over HTTPS). */
const isRefreshCookie = (cookie: Cookie): boolean => /refresh/i.test(cookie.name);

function load(): Partial<Record<Role, Cookie[]>> {
  try {
    return existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : {};
  } catch {
    return {};
  }
}

function store(sessions: Partial<Record<Role, Cookie[]>>): void {
  mkdirSync(CACHE_DIR, { recursive: true });
  writeFileSync(file, JSON.stringify(sessions));
}

/** The kept cookies of a role, removed from the cache because the API will rotate them on first use. */
export function takeSession(role: Role): Cookie[] | undefined {
  const sessions = load();
  const cookies = sessions[role];
  if (cookies === undefined) {
    return undefined;
  }
  delete sessions[role];
  store(sessions);
  return cookies;
}

/** Keeps the newest refresh cookie of a role's context (nothing when it signed out or the context is already closed). */
export async function keepSession(role: Role, context: BrowserContext): Promise<void> {
  let cookies: Cookie[];
  try {
    cookies = (await context.cookies()).filter(isRefreshCookie);
  } catch {
    return;
  }
  const sessions = load();
  if (cookies.length > 0) {
    sessions[role] = cookies;
  } else {
    delete sessions[role];
  }
  store(sessions);
}
