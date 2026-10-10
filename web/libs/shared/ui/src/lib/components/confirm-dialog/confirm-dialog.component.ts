import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  input,
  model,
  numberAttribute,
  output
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiDialogComponent } from '../dialog/dialog.component';
import { UiFormFieldComponent } from '../form-field/form-field.component';
import { UiTextareaComponent } from '../textarea/textarea.component';

/**
 * Confirmation for irreversible actions (post, reverse, reject, cancel, discard). Destructive confirmations never
 * focus the confirm button; the reason box (optional or required) is shown when `reasonLabel` is set.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [
    ReactiveFormsModule,
    UiButtonComponent,
    UiDialogComponent,
    UiFormFieldComponent,
    UiTextareaComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-dialog.component.html',
  styleUrl: './confirm-dialog.component.scss'
})
export class UiConfirmDialogComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly uiText = inject(UI_TEXT);

  $open = model(false, { alias: 'open' });
  $heading = input.required<string>({ alias: 'heading' });
  $body = input('', { alias: 'body' });
  $confirmLabel = input.required<string>({ alias: 'confirmLabel' });
  $cancelLabel = input.required<string>({ alias: 'cancelLabel' });
  $destructive = input(false, { alias: 'destructive', transform: booleanAttribute });
  $busy = input(false, { alias: 'busy', transform: booleanAttribute });
  $reasonLabel = input<string | undefined>(undefined, { alias: 'reasonLabel' });
  $reasonRequired = input(false, { alias: 'reasonRequired', transform: booleanAttribute });
  $minReasonLength = input(0, { alias: 'minReasonLength', transform: numberAttribute });

  $confirmed = output<string>({ alias: 'confirmed' });
  $cancelled = output<void>({ alias: 'cancelled' });

  readonly reason = new FormControl('', { nonNullable: true });

  $hasReason = computed(() => this.$reasonLabel() !== undefined);
  /** An empty or too short reason says what a reason must be, not just "required" (E-table copy). */
  $reasonMessages = computed(() => {
    const message = this.uiText().reasonTooShort(this.$minReasonLength());
    return { required: message, minlength: message };
  });
  $confirmVariant = computed(() => (this.$destructive() ? 'danger' : 'primary'));

  private readonly resetOnOpen = effect(() => {
    if (!this.$open()) {
      return;
    }
    const validators = this.$reasonRequired()
      ? [Validators.required, Validators.minLength(this.$minReasonLength())]
      : [];
    this.reason.setValidators(validators);
    this.reason.reset('');
  });

  onConfirm(): void {
    this.reason.markAsTouched();
    if (this.reason.invalid) {
      // Back to the box that needs the text, not left on the button that was pressed (EA-06).
      afterNextRender(() => this.host.nativeElement.querySelector('textarea')?.focus(), {
        injector: this.injector
      });
      return;
    }
    this.$confirmed.emit(this.reason.value.trim());
  }

  onCancel(): void {
    this.$open.set(false);
    this.$cancelled.emit();
  }

  onOpenChange(open: boolean): void {
    this.$open.set(open);
    if (!open) {
      this.$cancelled.emit();
    }
  }
}
