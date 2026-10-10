import { Injectable, inject } from '@angular/core';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { isMessageKey } from '@ims/core/i18n';
import { PageTitleService } from './page-title.service';

/**
 * Routes declare `title: 'receipts.title'` (a message key) instead of a literal, so tab titles are translated and
 * switch with the language. Every route change also feeds the live region of the shell (EA-03).
 */
@Injectable({ providedIn: 'root' })
export class PageTitleStrategy extends TitleStrategy {
  private readonly pageTitle = inject(PageTitleService);
  private navigations = 0;

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot);
    this.pageTitle.setScreen(isMessageKey(key) ? key : undefined, this.navigations > 0);
    this.navigations += 1;
  }
}
