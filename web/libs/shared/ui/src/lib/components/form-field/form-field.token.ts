import { InjectionToken, Signal } from '@angular/core';

/** What a form control needs from its surrounding <app-form-field> to be labelled and described. */
export interface FormFieldContext {
  readonly inputId: string;
  /** The id of the visible label, for a control that has a second part (a button) to name after it. */
  readonly labelId: string;
  readonly describedBy: Signal<string | null>;
  readonly invalid: Signal<boolean>;
  /** The field is marked required: the control states it itself (D4); the label only draws the asterisk. */
  readonly required: Signal<boolean>;
}

export const FORM_FIELD = new InjectionToken<FormFieldContext>('FORM_FIELD');
