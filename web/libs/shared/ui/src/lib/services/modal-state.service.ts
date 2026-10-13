import { Injectable, computed, signal } from '@angular/core';

/** How many modal dialogs are open: the toast host moves into the dialog meanwhile (a modal makes the page behind it inert). */
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
