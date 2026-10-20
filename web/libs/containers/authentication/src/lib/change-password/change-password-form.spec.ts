import { MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from '@ims/models';
import { SAME_AS_CURRENT_ERROR, createChangePasswordForm } from './change-password-form';

describe('change password form checks', () => {
  const validCurrent = 'the-old-password';
  const validNew = 'a-brand-new-password';

  function filled(current: string, next: string) {
    const form = createChangePasswordForm();
    form.setValue({ currentPassword: current, newPassword: next });
    return form;
  }

  it('accepts a different new password of 12 characters or more', () => {
    expect(filled(validCurrent, validNew).valid).toBe(true);
    expect(filled(validCurrent, 'x'.repeat(MIN_PASSWORD_LENGTH)).valid).toBe(true);
  });

  it('asks for both passwords', () => {
    const form = createChangePasswordForm();

    expect(form.controls.currentPassword.errors).toEqual({ required: true });
    expect(form.controls.newPassword.errors).toEqual({ required: true });
  });

  it('refuses a new password shorter than 12 characters, and takes exactly 12', () => {
    const short = filled(validCurrent, 'x'.repeat(MIN_PASSWORD_LENGTH - 1));

    expect(short.controls.newPassword.errors?.['minlength']).toEqual({
      requiredLength: MIN_PASSWORD_LENGTH,
      actualLength: MIN_PASSWORD_LENGTH - 1
    });
    expect(filled(validCurrent, 'x'.repeat(MIN_PASSWORD_LENGTH)).controls.newPassword.valid).toBe(
      true
    );
  });

  it('refuses a password longer than 128 characters, and takes exactly 128', () => {
    expect(
      filled(validCurrent, 'x'.repeat(MAX_PASSWORD_LENGTH + 1)).controls.newPassword.errors
    ).toEqual({
      maxlength: { requiredLength: MAX_PASSWORD_LENGTH, actualLength: MAX_PASSWORD_LENGTH + 1 }
    });
    expect(filled(validCurrent, 'x'.repeat(MAX_PASSWORD_LENGTH)).controls.newPassword.valid).toBe(
      true
    );
    expect(
      filled('y'.repeat(MAX_PASSWORD_LENGTH + 1), validNew).controls.currentPassword.errors?.[
        'maxlength'
      ]
    ).toBeTruthy();
  });

  it('refuses a new password equal to the current one', () => {
    const form = filled(validNew, validNew);

    expect(form.controls.newPassword.errors).toEqual({ [SAME_AS_CURRENT_ERROR]: true });
    expect(form.valid).toBe(false);
  });

  it('judges the new password again when the current one changes afterwards', () => {
    const form = filled(validNew, validNew);
    expect(form.controls.newPassword.hasError(SAME_AS_CURRENT_ERROR)).toBe(true);

    form.controls.currentPassword.setValue(validCurrent);
    expect(form.controls.newPassword.valid).toBe(true);

    form.controls.currentPassword.setValue(validNew);
    expect(form.controls.newPassword.hasError(SAME_AS_CURRENT_ERROR)).toBe(true);
  });

  it('compares exactly: a different case or a trailing space is a different password', () => {
    expect(filled('Correct-Horse-Battery', 'correct-horse-battery').valid).toBe(true);
    expect(filled('correct-horse-battery', 'correct-horse-battery ').valid).toBe(true);
  });

  it('does not report "same" for two empty fields (that is "required")', () => {
    const form = createChangePasswordForm();

    expect(form.controls.newPassword.hasError(SAME_AS_CURRENT_ERROR)).toBe(false);
  });
});
