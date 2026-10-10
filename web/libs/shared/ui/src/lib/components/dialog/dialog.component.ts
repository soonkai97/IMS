import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  model,
  viewChild
} from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiIconComponent } from '../icon/icon.component';

let nextDialogId = 0;

/**
 * Modal on top of the native <dialog>: focus is trapped and restored, Esc closes, the page behind is inert.
 * Phones get a bottom sheet. Content is rendered only while open, so forms start fresh each time.
 * Put buttons in `<div dialogActions>`; mark the first field `autofocus`.
 */
@Component({
  selector: 'app-dialog',
  imports: [UiButtonComponent, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dialog.component.html',
  styleUrl: './dialog.component.scss'
})
export class UiDialogComponent {
  private readonly uiText = inject(UI_TEXT);

  $open = model(false, { alias: 'open' });
  $heading = input.required<string>({ alias: 'heading' });

  private readonly $dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  $closeLabel = computed(() => this.uiText().close);

  readonly titleId = `app-dialog-title-${nextDialogId++}`;

  private readonly syncOpenState = effect(() => {
    const dialog = this.$dialog().nativeElement;
    if (this.$open() && !dialog.open) {
      this.show(dialog);
    }
    if (!this.$open() && dialog.open) {
      dialog.close();
    }
  });

  onNativeClose(): void {
    this.$open.set(false);
  }

  onCloseClick(): void {
    this.$open.set(false);
  }

  private show(dialog: HTMLDialogElement): void {
    if (typeof dialog.showModal === 'function') {
      dialog.showModal();
      return;
    }
    dialog.setAttribute('open', '');
  }
}
