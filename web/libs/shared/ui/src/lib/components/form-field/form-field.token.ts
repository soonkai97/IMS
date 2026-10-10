import { InjectionToken, Signal } from '@angular/core';

/** What a form control needs from its surrounding <app-form-field> to be labelled and described. */
export interface FormFieldContext {
  readonly inputId: string;
  readonly describedBy: Signal<string | null>;
  readonly invalid: Signal<boolean>;
}

export const FORM_FIELD = new InjectionToken<FormFieldContext>('FORM_FIELD');
