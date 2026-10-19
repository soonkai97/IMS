import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  forwardRef,
  inject,
  input
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl } from '@angular/forms';
import { merge, of, startWith, switchMap } from 'rxjs';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiIconComponent } from '../icon/icon.component';
import { FORM_FIELD, FormFieldContext } from './form-field.token';

let nextFieldId = 0;

/**
 * Label above the control, hint and error text below. The label stays visible, a required field gets a red
 * asterisk (aria-hidden) and the control itself carries `required`, so a screen reader says "<label>, required" from
 * the state and not twice (D4, docs/design/2026-refresh-ux.md 3.3), and the error is tied to the control with
 * aria-describedby.
 */
@Component({
  selector: 'app-form-field',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './form-field.component.html',
  styleUrl: './form-field.component.scss',
  providers: [{ provide: FORM_FIELD, useExisting: forwardRef(() => UiFormFieldComponent) }]
})
export class UiFormFieldComponent implements FormFieldContext {
  private readonly uiText = inject(UI_TEXT);

  $label = input.required<string>({ alias: 'label' });
  $hint = input<string | undefined>(undefined, { alias: 'hint' });
  $required = input(false, { alias: 'required', transform: booleanAttribute });
  /** Reactive-forms control whose errors are shown once it was touched. */
  $control = input<AbstractControl | null>(null, { alias: 'control' });
  /** Explicit error text (wins over the control's own errors). */
  $error = input<string | null>(null, { alias: 'error' });
  /** Field-specific wording per error key ("min": "Enter a quantity above 0.") before the generic sentence. */
  $messages = input<Readonly<Record<string, string>>>({}, { alias: 'messages' });

  /** Id of another element that describes this field too (a notice about the whole page), added to aria-describedby. */
  $extraDescription = input<string | null>(null, { alias: 'extraDescription' });

  readonly inputId = `app-field-${nextFieldId++}`;
  readonly labelId = `${this.inputId}-label`;
  readonly hintId = `${this.inputId}-hint`;
  readonly errorId = `${this.inputId}-error`;

  private readonly $controlEvents = toSignal(
    toObservable(this.$control).pipe(
      switchMap(control => (control ? merge(control.events, of(null)) : of(null))),
      startWith(null)
    ),
    { initialValue: null }
  );

  $text = computed(() => this.uiText());
  $errorText = computed(() => {
    this.$controlEvents();
    const explicit = this.$error();
    if (explicit) {
      return explicit;
    }
    const control = this.$control();
    if (!control?.touched || !control.errors) {
      return null;
    }
    const [key, value] = Object.entries(control.errors)[0];
    return this.$messages()[key] ?? this.$text().validationMessage(key, value);
  });
  readonly describedBy = computed(() => {
    const ids = [
      this.$hint() ? this.hintId : null,
      this.$errorText() ? this.errorId : null,
      this.$extraDescription()
    ];
    const joined = ids.filter(Boolean).join(' ');
    return joined.length > 0 ? joined : null;
  });
  readonly invalid = computed(() => this.$errorText() !== null);
  /** Read by the control inside this field, which then carries the `required` attribute itself. */
  readonly required = computed(() => this.$required());
}
