import {
  ChangeDetectionStrategy,
  Component,
  computed,
  forwardRef,
  inject,
  input,
  signal
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { FORM_FIELD } from '../form-field/form-field.token';
import { UiIconComponent } from '../icon/icon.component';

export type SelectValue = string | number | boolean | null;

export interface SelectOption {
  readonly value: SelectValue;
  readonly label: string;
  readonly disabled?: boolean;
}

interface SelectItem extends SelectOption {
  readonly index: number;
  readonly selected: boolean;
}

/** Native <select> with typed values (numbers stay numbers). `placeholder` adds an empty first option (value null). */
@Component({
  selector: 'app-select',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './select.component.html',
  styleUrl: './select.component.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => UiSelectComponent), multi: true }
  ]
})
export class UiSelectComponent implements ControlValueAccessor {
  private readonly field = inject(FORM_FIELD, { optional: true });
  private readonly uiText = inject(UI_TEXT);

  $options = input.required<readonly SelectOption[]>({ alias: 'options' });
  /** Text of the empty option. `undefined` = no empty option, `''` = use the default "Select...". */
  $placeholder = input<string | undefined>(undefined, { alias: 'placeholder' });
  $ariaLabel = input<string | undefined>(undefined, { alias: 'ariaLabel' });

  $value = signal<SelectValue>(null);
  $disabled = signal(false);

  $id = computed(() => this.field?.inputId ?? null);
  $describedBy = computed(() => this.field?.describedBy() ?? null);
  $invalid = computed(() => this.field?.invalid() ?? false);
  $required = computed(() => this.field?.required() ?? false);
  $controlClass = computed(() => toClassStr(['ui-select-control', this.$invalid() && '--invalid']));
  $placeholderText = computed(() => {
    const placeholder = this.$placeholder();
    if (placeholder === undefined) {
      return null;
    }
    return placeholder.length > 0 ? placeholder : this.uiText().selectPlaceholder;
  });
  $items = computed<readonly SelectItem[]>(() => {
    const value = this.$value();
    return this.$options().map((option, index) => ({
      ...option,
      index,
      selected: option.value === value
    }));
  });
  $placeholderSelected = computed(() => this.$items().every(item => !item.selected));

  private onChange: (value: SelectValue) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: unknown): void {
    this.$value.set(value === undefined ? null : (value as SelectValue));
  }

  registerOnChange(fn: (value: SelectValue) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.$disabled.set(isDisabled);
  }

  onChangeEvent(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }
    const raw = event.target.value;
    const option = raw === '' ? undefined : this.$options()[Number(raw)];
    const value = option?.value ?? null;
    this.$value.set(value);
    this.onChange(value);
  }

  onBlur(): void {
    this.onTouched();
  }
}
