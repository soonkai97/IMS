import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter, tap } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';

/** Name part of a service-worker cache that holds API responses (ngsw `dataGroups`; none is configured any more). */
const DATA_CACHE_MARKER = ':data:';

/**
 * PWA glue: asks the user to reload when a new version is ready.
 *
 * The service worker caches the app shell only and never answers an API call (ngsw-config.json has no `dataGroups`;
 * docs/architecture/frontend.md "Service worker and API data"). Deleting a data cache from the page cannot be relied on
 * (the worker keeps its own handle on it and goes on answering from it), which is why the data groups are gone. The cleanup
 * below is a best-effort extra: it removes data caches left by an older release, at start and when the session ends.
 */
@Injectable({ providedIn: 'root' })
export class PwaService {
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);
  private readonly update = inject(SwUpdate);

  start(): void {
    if (this.update.isEnabled) {
      this.update.versionUpdates
        .pipe(
          filter((event): event is VersionReadyEvent => event.type === 'VERSION_READY'),
          tap(() =>
            this.toast.info(this.i18n.t('pwa.updateReady'), {
              label: this.i18n.t('pwa.reload'),
              run: () => document.location.reload()
            })
          ),
          takeUntilDestroyed()
        )
        .subscribe();
    }
    void this.clearDataCaches();
    this.auth.sessionEnded$
      .pipe(
        tap(() => void this.clearDataCaches()),
        takeUntilDestroyed()
      )
      .subscribe();
  }

  /** Deletes every cache whose name marks it as API data. Never throws: storage can be blocked. */
  async clearDataCaches(): Promise<void> {
    if (typeof caches === 'undefined') {
      return;
    }
    try {
      const names = await caches.keys();
      await Promise.all(
        names.filter(name => name.includes(DATA_CACHE_MARKER)).map(name => caches.delete(name))
      );
    } catch {
      // Best effort: the worker no longer caches API data, so a failure here leaves nothing exposed.
    }
  }
}
