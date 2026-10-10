import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  computed,
  forwardRef,
  inject,
  input,
  signal,
  viewChild
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiIconComponent } from '../icon/icon.component';
import { FORM_FIELD } from '../form-field/form-field.token';

export type InputType = 'text' | 'password' | 'email' | 'number' | 'date' | 'tel';

/** Text-like input. `type="number"` writes `number | null` to the form; every other type writes a string. */
@Component({
  selector: 'app-input',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './input.component.html',
  styleUrl: './input.component.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => UiInputComponent), multi: true }
  ]
})
export class UiInputComponent implements ControlValueAccessor {
  private readonly field = inject(FORM_FIELD, { optional: true });
  private readonly uiText = inject(UI_TEXT);

  $type = input<InputType>('text', { alias: 'type' });
  $placeholder = input('', { alias: 'placeholder' });
  $inputMode = input<'text' | 'decimal' | 'numeric' | 'email' | 'tel' | undefined>(undefined, {
    alias: 'inputMode'
  });
  $autocomplete = input<string | undefined>(undefined, { alias: 'autocomplete' });
  $min = input<number | string | undefined>(undefined, { alias: 'min' });
  $max = input<number | string | undefined>(undefined, { alias: 'max' });
  $step = input<number | string | undefined>(undefined, { alias: 'step' });
  $maxLength = input<number | undefined>(undefined, { alias: 'maxLength' });
  $readonly = input(false, { alias: 'readonly', transform: booleanAttribute });
  $ariaLabel = input<string | undefined>(undefined, { alias: 'ariaLabel' });

  $control = viewChild.required<ElementRef<HTMLInputElement>>('control');

  $value = signal('');
  $disabled = signal(false);
  $revealed = signal(false);

  $id = computed(() => this.field?.inputId ?? null);
  $describedBy = computed(() => this.field?.describedBy() ?? null);
  $invalid = computed(() => this.field?.invalid() ?? false);
  $isPassword = computed(() => this.$type() === 'password');
  $nativeType = computed(() => (this.$isPassword() && this.$revealed() ? 'text' : this.$type()));
  $toggleLabel = computed(() =>
    this.$revealed() ? this.uiText().hidePassword : this.uiText().showPassword
  );
  $controlClass = computed(() => toClassStr(['ui-input-control', this.$invalid() && '--invalid']));

  private onChange: (value: string | number | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  focus(): void {
    this.$control().nativeElement.focus();
  }

  writeValue(value: unknown): void {
    this.$value.set(value === null || value === undefined ? '' : String(value));
  }

  registerOnChange(fn: (value: string | number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.$disabled.set(isDisabled);
  }

  onInput(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }
    const raw = event.target.value;
    this.$value.set(raw);
    this.onChange(this.toModelValue(raw));
  }

  onBlur(): void {
    this.onTouched();
  }

  onToggleReveal(): void {
    this.$revealed.update(revealed => !revealed);
  }

  private toModelValue(raw: string): string | number | null {
    if (this.$type() !== 'number') {
      return raw;
    }
    if (raw.trim().length === 0) {
      return null;
    }
    const parsed = Number(raw);
    return Number.isNaN(parsed) ? null : parsed;
  }
}
