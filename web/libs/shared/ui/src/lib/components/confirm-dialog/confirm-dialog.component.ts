import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  effect,
  input,
  model,
  numberAttribute,
  output
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
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
