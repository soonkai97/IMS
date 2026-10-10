/**
 * Starts nothing. Checks that the API and the web app answer and that the demo passwords are set, so a missing piece is
 * one clear message instead of thirty timeouts. Passwords are only ever checked for presence.
 */
import { API_URL, WEB_URL, missingPasswordVariables } from './config';

async function reachable(url: string): Promise<string | null> {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(10_000) });
    return response.ok ? null : `${url} answered HTTP ${response.status}`;
  } catch (error) {
    return `${url} is not reachable (${(error as Error).message})`;
  }
}

export default async function globalSetup(): Promise<void> {
  const problems = (
    await Promise.all([reachable(`${API_URL}/health`), reachable(`${WEB_URL}/login`)])
  ).filter((problem): problem is string => problem !== null);
  const missing = missingPasswordVariables();
  if (missing.length > 0) {
    problems.push(`Missing environment variables: ${missing.join(', ')}`);
  }
  if (problems.length > 0) {
    throw new Error(
      `E2E prerequisites are not met (this suite starts nothing itself, see e2e/README.md):\n- ${problems.join('\n- ')}`
    );
  }
}
