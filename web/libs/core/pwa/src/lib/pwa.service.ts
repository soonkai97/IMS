import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter, tap } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';

/** Service-worker cache names that hold API responses (ngsw-config.json dataGroups). */
const DATA_CACHE_MARKER = ':data:';

/**
 * PWA glue: asks the user to reload when a new version is ready, and drops cached API data when the session ends so
 * the next user on a shared device never sees the previous user's master data.
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
    this.auth.sessionEnded$
      .pipe(
        tap(() => void this.clearDataCaches()),
        takeUntilDestroyed()
      )
      .subscribe();
  }

  private async clearDataCaches(): Promise<void> {
    if (typeof caches === 'undefined') {
      return;
    }
    const names = await caches.keys();
    await Promise.all(
      names.filter(name => name.includes(DATA_CACHE_MARKER)).map(name => caches.delete(name))
    );
  }
}
