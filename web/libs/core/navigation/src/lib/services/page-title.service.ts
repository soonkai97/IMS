import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { I18nService, MessageKey } from '@ims/core/i18n';

/**
 * The browser tab title and the screen-reader announcement of a route change (WCAG 2.4.2 Page Titled).
 * Title = "{detail} - {screen} - {app}". The screen comes from the route (`title` = a message key, so it follows the
 * language); the detail is set by a page that knows more, such as the document number.
 */
@Injectable({ providedIn: 'root' })
export class PageTitleService {
  private readonly browserTitle = inject(Title);
  private readonly i18n = inject(I18nService);

  private readonly $screenKey = signal<MessageKey | undefined>(undefined);
  private readonly $detail = signal<string | undefined>(undefined);
  private readonly $announced = signal('');

  readonly $title = computed(() => {
    const key = this.$screenKey();
    const parts = [this.$detail(), key ? this.i18n.t(key) : undefined, this.i18n.t('app.name')];
    return parts.filter((part): part is string => !!part).join(' - ');
  });

  /** What a live region says after a route change; empty until the first navigation after start-up. */
  readonly $announcement = this.$announced.asReadonly();

  private readonly syncBrowserTitle = effect(() => this.browserTitle.setTitle(this.$title()));

  /** A route finished navigating; `announce` is false for the first page load (the browser reads the title itself). */
  setScreen(key: MessageKey | undefined, announce: boolean): void {
    this.$screenKey.set(key);
    if (announce) {
      this.$announced.set(this.$title());
    }
  }

  /** The page knows what it shows ("RCV-WH01-2610-0007", "New receipt"); undefined = nothing more to say. */
  setDetail(detail: string | undefined): void {
    this.$detail.set(detail);
  }
}
