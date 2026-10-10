import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  forwardRef,
  inject,
  input,
  signal
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { toClassStr } from '@ims/utils';
import { FORM_FIELD } from '../form-field/form-field.token';

@Component({
  selector: 'app-textarea',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './textarea.component.html',
  styleUrl: './textarea.component.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => UiTextareaComponent), multi: true }
  ]
})
export class UiTextareaComponent implements ControlValueAccessor {
  private readonly field = inject(FORM_FIELD, { optional: true });

  $rows = input(3, { alias: 'rows' });
  $placeholder = input('', { alias: 'placeholder' });
  $maxLength = input<number | undefined>(undefined, { alias: 'maxLength' });
  $readonly = input(false, { alias: 'readonly', transform: booleanAttribute });
  /** Focused when its dialog opens (the dialog looks for `[data-autofocus]`). */
  $focusOnOpen = input(false, { alias: 'focusOnOpen', transform: booleanAttribute });

  $value = signal('');
  $disabled = signal(false);

  $id = computed(() => this.field?.inputId ?? null);
  $describedBy = computed(() => this.field?.describedBy() ?? null);
  $invalid = computed(() => this.field?.invalid() ?? false);
  $controlClass = computed(() =>
    toClassStr(['ui-textarea-control', this.$invalid() && '--invalid'])
  );

  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: unknown): void {
    this.$value.set(typeof value === 'string' ? value : '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.$disabled.set(isDisabled);
  }

  onInput(event: Event): void {
    if (!(event.target instanceof HTMLTextAreaElement)) {
      return;
    }
    this.$value.set(event.target.value);
    this.onChange(event.target.value);
  }

  onBlur(): void {
    this.onTouched();
  }
}
