import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';
import { PwaService } from './pwa.service';

const SHELL_CACHE = 'ngsw:/:abc123:assets:app:cache';
const DATA_CACHE = 'ngsw:/:2:data:master-data:cache';
const DATA_META = 'ngsw:/:2:data:master-data:lru';

describe('PwaService', () => {
  const sessionEnded = new Subject<void>();
  let spectator: SpectatorService<PwaService>;
  let deleted: string[];
  let names: string[];
  const createService = createServiceFactory({
    service: PwaService,
    providers: [
      mockProvider(AuthService, { sessionEnded$: sessionEnded.asObservable() }),
      mockProvider(I18nService),
      mockProvider(ToastService),
      mockProvider(SwUpdate, { isEnabled: false })
    ]
  });

  /** start() runs in the app's injection context (see app.config.ts). */
  const start = () => TestBed.runInInjectionContext(() => spectator.service.start());
  const settle = () => new Promise<void>(resolve => setTimeout(resolve));

  beforeEach(() => {
    deleted = [];
    names = [SHELL_CACHE, DATA_CACHE, DATA_META];
    Object.defineProperty(globalThis, 'caches', {
      configurable: true,
      value: {
        keys: jest.fn(async () => names),
        delete: jest.fn(async (name: string) => {
          deleted.push(name);
          return true;
        })
      }
    });
    spectator = createService();
  });

  afterEach(() => {
    delete (globalThis as { caches?: unknown }).caches;
  });

  it('removes the API data caches of an older release at start and keeps the app shell cache', async () => {
    start();
    await settle();

    expect([...new Set(deleted)].sort()).toEqual([DATA_CACHE, DATA_META].sort());
    expect(deleted).not.toContain(SHELL_CACHE);
  });

  it('removes them again when the session ends', async () => {
    start();
    await settle();
    deleted.length = 0;

    sessionEnded.next();
    await settle();

    expect([...new Set(deleted)].sort()).toEqual([DATA_CACHE, DATA_META].sort());
  });

  it('does not throw when Cache Storage is blocked', async () => {
    (globalThis.caches.keys as jest.Mock).mockRejectedValue(new Error('SecurityError'));

    await expect(spectator.service.clearDataCaches()).resolves.toBeUndefined();
  });

  it('does nothing where Cache Storage does not exist', async () => {
    delete (globalThis as { caches?: unknown }).caches;

    await expect(spectator.service.clearDataCaches()).resolves.toBeUndefined();
  });
});
