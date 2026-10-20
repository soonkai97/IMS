import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { TaxFieldShape } from '@ims/models';

/** The error key of a value that breaks the shape (too long, or a character that is not allowed). */
export const TAX_SHAPE_ERROR = 'taxShape';

/** What the server stores: the value without the spaces at its ends, empty when blank. */
export function cleanTaxField(value: string | null | undefined): string {
  return (value ?? '').trim();
}

/**
 * The shape rule of the server for one tax-ready field, on the trimmed value (the server trims before it checks, so a space at an end is
 * not an error and does not count). Blank is fine: the field is optional. Nothing is cut or changed while typing.
 */
export function taxFieldShape(shape: TaxFieldShape): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = cleanTaxField(typeof control.value === 'string' ? control.value : '');
    if (value.length === 0) {
      return null;
    }
    return value.length > shape.maxLength || !shape.pattern.test(value)
      ? { [TAX_SHAPE_ERROR]: true }
      : null;
  };
}
