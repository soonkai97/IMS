import { readFileSync } from 'node:fs';
import { join } from 'node:path';

interface NgswConfig {
  readonly dataGroups?: unknown;
  readonly assetGroups: readonly {
    readonly resources?: { readonly files?: readonly string[]; readonly urls?: readonly string[] };
  }[];
}

/**
 * QA round S1: the service worker kept API answers for 12 h, served them after sign-out with no token and hid new data.
 * The worker may cache the app shell only; it must never be configured to answer an API call.
 */
describe('ngsw-config.json', () => {
  const config = JSON.parse(
    readFileSync(join(__dirname, '..', '..', 'ngsw-config.json'), 'utf8')
  ) as NgswConfig;

  it('has no data group, so no API call is ever answered from a cache', () => {
    expect(config.dataGroups).toBeUndefined();
  });

  it('lists no API address among the cached files or urls', () => {
    const listed = config.assetGroups.flatMap(group => [
      ...(group.resources?.files ?? []),
      ...(group.resources?.urls ?? [])
    ]);

    expect(listed.length).toBeGreaterThan(0);
    expect(listed.filter(entry => entry.includes('/api'))).toEqual([]);
  });
});
