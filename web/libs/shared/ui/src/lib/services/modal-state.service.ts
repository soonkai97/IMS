import { Injectable, computed, signal } from '@angular/core';

/**
 * How many modal dialogs are open. A modal makes everything outside it inert, so a message that must stay readable and
 * clickable (the toast host) moves into the dialog while one is open and leaves the page's own copy empty.
 */
@Injectable({ providedIn: 'root' })
export class UiModalState {
  private readonly $count = signal(0);

  readonly $isOpen = computed(() => this.$count() > 0);

  enter(): void {
    this.$count.update(count => count + 1);
  }

  leave(): void {
    this.$count.update(count => Math.max(0, count - 1));
  }
}
