import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  viewChild
} from '@angular/core';
import { UiModalState } from '../../services/modal-state.service';
import { UI_DIALOG_FEEDBACK } from '../../tokens/dialog-feedback.token';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiIconComponent } from '../icon/icon.component';

let nextDialogId = 0;

/**
 * Native controls only: the host element of a wrapper component (`<app-search-input focusOnOpen>`) is not focusable.
 * The attribute is `data-autofocus` because the real `autofocus` is banned by lint and the browser would ignore it
 * anyway (the panel is rendered after the dialog opens).
 */
const AUTOFOCUS_SELECTOR =
  'input[data-autofocus], textarea[data-autofocus], select[data-autofocus]';

/**
 * Modal on top of the native <dialog>: focus is trapped and restored, Esc closes, the page behind is inert.
 * Phones get a bottom sheet. Content is rendered only while open, so forms start fresh each time.
 * Put buttons in `<div dialogActions>`; mark the first field `data-autofocus`.
 */
@Component({
  selector: 'app-dialog',
  imports: [NgComponentOutlet, UiButtonComponent, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dialog.component.html',
  styleUrl: './dialog.component.scss'
})
export class UiDialogComponent {
  private readonly injector = inject(Injector);
  private readonly uiText = inject(UI_TEXT);
  private readonly modalState = inject(UiModalState);

  /** Rendered above the content while open: failures raised meanwhile are shown here, not behind the backdrop. */
  readonly feedback = inject(UI_DIALOG_FEEDBACK);
  readonly feedbackInputs = { inDialog: true };

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
      this.hide(dialog);
    }
  });

  private readonly trackModal = effect(onCleanup => {
    if (this.$open()) {
      this.modalState.enter();
      onCleanup(() => this.modalState.leave());
    }
  });

  onNativeClose(): void {
    this.$open.set(false);
  }

  onCloseClick(): void {
    this.$open.set(false);
  }

  private hide(dialog: HTMLDialogElement): void {
    if (typeof dialog.close === 'function') {
      dialog.close();
      return;
    }
    dialog.removeAttribute('open');
  }

  private show(dialog: HTMLDialogElement): void {
    if (typeof dialog.showModal === 'function') {
      dialog.showModal();
    } else {
      dialog.setAttribute('open', '');
    }
    // The panel is rendered after the dialog opens, so the browser's own autofocus step finds nothing: focus the
    // marked field once the content exists (NV-03: a scanner types at once; otherwise focus sits on the <dialog>).
    afterNextRender(() => dialog.querySelector<HTMLElement>(AUTOFOCUS_SELECTOR)?.focus(), {
      injector: this.injector
    });
  }
}
