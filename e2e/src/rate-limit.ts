/**
 * The API allows sign-in 20 requests a minute per client address (token refresh has its own, larger budget). A suite that
 * signs in a lot would trip it (HTTP 429). Most scenarios do not sign in at all (they restore a kept session, see
 * `session-cache.ts`); the ones that do spend from this budget. This keeps a small log of the sign-ins we make (in
 * e2e/.cache, so a restarted worker and a second run see it too) and waits before the budget is used up. It never talks to
 * the API itself.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { API_URL } from './config';
import { CACHE_DIR } from './session-cache';

const WINDOW_MS = 60_000;
/** Stay under the server's 20 so the odd unplanned sign-in still fits. */
const BUDGET = 15;
const logFile = join(
  CACHE_DIR,
  `limiter-${Buffer.from(API_URL).toString('hex').slice(0, 24)}.json`
);

function recent(now: number): { at: number; cost: number }[] {
  try {
    const all = existsSync(logFile)
      ? (JSON.parse(readFileSync(logFile, 'utf8')) as { at: number; cost: number }[])
      : [];
    return all.filter(entry => now - entry.at < WINDOW_MS);
  } catch {
    return [];
  }
}

/** Waits until `cost` sign-ins fit in the budget, then records them. */
export async function spendLoginBudget(cost: number): Promise<void> {
  for (;;) {
    const now = Date.now();
    const entries = recent(now);
    const used = entries.reduce((sum, entry) => sum + entry.cost, 0);
    if (used + cost <= BUDGET) {
      mkdirSync(CACHE_DIR, { recursive: true });
      writeFileSync(logFile, JSON.stringify([...entries, { at: now, cost }]));
      return;
    }
    const oldest = entries[0]?.at ?? now;
    await new Promise(resolve =>
      setTimeout(resolve, Math.max(500, oldest + WINDOW_MS - now + 100))
    );
  }
}
