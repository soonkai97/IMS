import { Injectable, computed, inject } from '@angular/core';
import { SettingsClient, SettingsDto } from '@ims/api';
import { QueryLoader } from '@ims/utils';

/** Server-side rules every screen may need (negative stock, self-approval). Loaded on demand, shared by all screens. */
@Injectable({ providedIn: 'root' })
export class SystemSettingsStore {
  private readonly client = inject(SettingsClient);

  readonly settings = new QueryLoader<void, SettingsDto>(() => this.client.get());

  readonly $allowNegativeStock = computed(() => this.settings.$data()?.allowNegativeStock ?? false);
  readonly $allowSelfApproval = computed(() => this.settings.$data()?.allowSelfApproval ?? false);

  /** Loads once; later calls reuse the result. Use reload() after the settings changed. */
  ensureLoaded(): void {
    if (this.settings.$data() === undefined && !this.settings.$isLoading()) {
      this.settings.load();
    }
  }

  reload(): void {
    this.settings.load();
  }

  /** Called by the administration feature after saving, so every screen sees the new values at once. */
  apply(settings: SettingsDto): void {
    this.settings.$data.set(settings);
  }
}
