/**
 * The API allows sign-in 20 requests a minute per client address (token refresh has its own, larger budget). A suite that
 * signs in a lot would trip it (HTTP 429). The log still counts the page boot (a refresh) as a cost, which is now more careful
 * than needed and harmless. This keeps a small log of the calls we make (in the temp folder, so a restarted worker and a
 * second run see it too) and waits before the budget is used up. It never talks to the API itself.
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { API_URL } from './config';

const WINDOW_MS = 60_000;
/** Stay under the server's 20 so the odd unplanned page load still fits. */
const BUDGET = 15;
const logFile = join(
  tmpdir(),
  `ims-e2e-limiter-${Buffer.from(API_URL).toString('hex').slice(0, 24)}.json`
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

/** Waits until `cost` calls fit in the budget, then records them. Sign-in through the form costs 2 (page boot + login). */
export async function spendLoginBudget(cost: number): Promise<void> {
  for (;;) {
    const now = Date.now();
    const entries = recent(now);
    const used = entries.reduce((sum, entry) => sum + entry.cost, 0);
    if (used + cost <= BUDGET) {
      writeFileSync(logFile, JSON.stringify([...entries, { at: now, cost }]));
      return;
    }
    const oldest = entries[0]?.at ?? now;
    await new Promise(resolve =>
      setTimeout(resolve, Math.max(500, oldest + WINDOW_MS - now + 100))
    );
  }
}
