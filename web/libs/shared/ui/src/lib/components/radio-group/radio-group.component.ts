import {
  ChangeDetectionStrategy,
  Component,
  computed,
  forwardRef,
  input,
  signal
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface RadioOption {
  readonly value: string | number;
  readonly label: string;
}

let nextGroupId = 0;

@Component({
  selector: 'app-radio-group',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './radio-group.component.html',
  styleUrl: './radio-group.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => UiRadioGroupComponent),
      multi: true
    }
  ]
})
export class UiRadioGroupComponent implements ControlValueAccessor {
  $options = input.required<readonly RadioOption[]>({ alias: 'options' });
  $legend = input.required<string>({ alias: 'legend' });

  readonly groupName = `app-radio-${nextGroupId++}`;

  $value = signal<string | number | null>(null);
  $disabled = signal(false);
  $items = computed(() =>
    this.$options().map(option => ({ ...option, checked: option.value === this.$value() }))
  );

  private onChange: (value: string | number) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: unknown): void {
    this.$value.set(typeof value === 'string' || typeof value === 'number' ? value : null);
  }

  registerOnChange(fn: (value: string | number) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.$disabled.set(isDisabled);
  }

  onSelect(value: string | number): void {
    this.$value.set(value);
    this.onChange(value);
    this.onTouched();
  }
}
