import { AbstractControl, FormGroup } from '@angular/forms';
import { ApiError } from '@ims/models';

export const SERVER_ERROR_KEY = 'server';

/** "lines[0].qty" -> "lines.0.qty" (the path syntax of FormGroup.get). */
function toControlPath(field: string): string {
  return field.replace(/\[(\d+)\]/g, '.$1');
}

/**
 * Puts the field messages of a 400 response on the matching form controls (error key "server") and returns the
 * messages that did not match any control, so the caller can show them in a banner.
 */
export function applyFieldErrors(form: FormGroup, error: ApiError): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control: AbstractControl | null = form.get(toControlPath(field));
    if (control) {
      control.setErrors({ [SERVER_ERROR_KEY]: messages[0] });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }
  return unmatched;
}
