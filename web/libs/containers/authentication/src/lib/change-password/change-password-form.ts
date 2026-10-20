import {
  AbstractControl,
  FormControl,
  FormGroup,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from '@ims/models';

/** Error key of "the new password is the one in use" (the API refuses it too, with the same meaning). */
export const SAME_AS_CURRENT_ERROR = 'sameAsCurrent';

/** The new password must differ from the current one. An empty value is left to `required`. */
function differsFromCurrent(control: AbstractControl): ValidationErrors | null {
  const current: unknown = control.parent?.get('currentPassword')?.value;
  return typeof control.value === 'string' && control.value.length > 0 && control.value === current
    ? { [SAME_AS_CURRENT_ERROR]: true }
    : null;
}

/**
 * The checks that run before anything is sent (the API repeats them): both passwords present, the new one 12 to 128 characters and
 * not equal to the current one. There is no "repeat" field on purpose: the eye button shows what was typed.
 */
export function createChangePasswordForm() {
  const form = new FormGroup({
    currentPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_PASSWORD_LENGTH)]
    }),
    newPassword: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.minLength(MIN_PASSWORD_LENGTH),
        Validators.maxLength(MAX_PASSWORD_LENGTH),
        differsFromCurrent
      ]
    })
  });
  // The rule reads the other field, so a change of the current password judges the new one again.
  form.controls.currentPassword.valueChanges.subscribe(() =>
    form.controls.newPassword.updateValueAndValidity()
  );
  return form;
}

export type ChangePasswordForm = ReturnType<typeof createChangePasswordForm>;
