import { Injectable, computed, signal } from '@angular/core';

/** Number of API calls in flight; drives the thin progress bar at the top of the shell. */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly $pending = signal(0);

  readonly $isLoading = computed(() => this.$pending() > 0);

  begin(): void {
    this.$pending.update(count => count + 1);
  }

  end(): void {
    this.$pending.update(count => Math.max(0, count - 1));
  }
}
