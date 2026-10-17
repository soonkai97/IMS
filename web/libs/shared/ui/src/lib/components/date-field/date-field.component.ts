import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  forwardRef,
  inject,
  signal,
  viewChild
} from '@angular/core';
import {
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator
} from '@angular/forms';
import { MIN_FILTER_DATE, formatDateText, parseDateText, toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { FORM_FIELD } from '../form-field/form-field.token';
import { UiIconComponent } from '../icon/icon.component';

/** The error key of text that is no date; the kit sentence for it is `validationMessage('date')`. */
const DATE_ERROR = 'date';

let nextButtonId = 0;

/**
 * A date in the app format `dd/MM/yyyy` ("09/10/2026", in every language). The form value is the
 * ISO day `yyyy-MM-dd` or an empty string, so URLs, requests and every other date in the code stay as they were. Also
 * reads `2026-10-09`, `09 Oct 2026` (month name in the active language) and `2026年10月9日`; the text is rewritten in the app
 * format when the field is left.
 *
 * The calendar button opens the browser's own date picker (`showPicker()` on a visually hidden date input, which
 * takes the language of the page): the picker brings its keyboard handling and screen-reader support, so the kit
 * ships no month grid of its own (R23).
 */
@Component({
  selector: 'app-date-field',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './date-field.component.html',
  styleUrl: './date-field.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => UiDateFieldComponent),
      multi: true
    },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => UiDateFieldComponent), multi: true }
  ]
})
export class UiDateFieldComponent implements ControlValueAccessor, Validator {
  private readonly field = inject(FORM_FIELD, { optional: true });
  private readonly uiText = inject(UI_TEXT);

  readonly minDate = MIN_FILTER_DATE;
  readonly buttonId = `app-date-button-${nextButtonId++}`;

  private readonly $text = viewChild.required<ElementRef<HTMLInputElement>>('text');
  private readonly $picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  /** yyyy-MM-dd or ''. */
  $value = signal('');
  /** What the user is typing; null while the text is the formatted value (so a language change rewrites it). */
  private readonly $typed = signal<string | null>(null);
  $disabled = signal(false);
  private unreadable = false;

  $locale = computed(() => this.uiText().locale);
  $displayed = computed(() => this.$typed() ?? formatDateText(this.$value()));
  $placeholder = computed(() => this.uiText().datePlaceholder);
  $chooseLabel = computed(() => this.uiText().chooseDate);
  $id = computed(() => this.field?.inputId ?? null);
  $describedBy = computed(() => this.field?.describedBy() ?? null);
  $invalid = computed(() => this.field?.invalid() ?? false);
  $required = computed(() => this.field?.required() ?? false);
  /** "Choose date", then the label of the field ("From"), so the two buttons of a filter bar are told apart. */
  $buttonLabelledBy = computed(() => {
    const labelId = this.field?.labelId;
    return labelId ? `${this.buttonId} ${labelId}` : null;
  });
  $controlClass = computed(() =>
    toClassStr(['ui-date-field-control', this.$invalid() && '--invalid'])
  );

  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  focus(): void {
    this.$text().nativeElement.focus();
  }

  writeValue(value: unknown): void {
    const iso = typeof value === 'string' ? value : '';
    this.$value.set(formatDateText(iso) === '' ? '' : iso);
    this.$typed.set(null);
    this.unreadable = false;
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

  validate(): ValidationErrors | null {
    return this.unreadable ? { [DATE_ERROR]: true } : null;
  }

  /** A date is applied as soon as it reads; half-typed text waits for the blur, so it filters nothing on the way. */
  onInput(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }
    const text = event.target.value;
    this.$typed.set(text);
    const iso = text.trim() === '' ? '' : parseDateText(text, this.$locale());
    this.unreadable = iso === null;
    if (iso !== null) {
      this.$value.set(iso);
      this.onChange(iso);
    }
  }

  onBlur(): void {
    if (this.unreadable) {
      // The old day no longer matches the text: drop it and let the validator say why.
      this.$value.set('');
      this.onChange('');
    } else {
      this.$typed.set(null);
    }
    this.onTouched();
  }

  onOpenPicker(): void {
    const picker = this.$picker().nativeElement;
    picker.value = this.$value();
    // Older browsers have no showPicker(); the text box still works there.
    if (typeof picker.showPicker === 'function') {
      try {
        picker.showPicker();
      } catch {
        // Refused outside a user gesture or inside a frame; nothing to recover.
      }
    }
  }

  onPick(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }
    const iso = event.target.value;
    this.unreadable = false;
    this.$typed.set(null);
    this.$value.set(iso);
    this.onChange(iso);
    this.onTouched();
  }
}
