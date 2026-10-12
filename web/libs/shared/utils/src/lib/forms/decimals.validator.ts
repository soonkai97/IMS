import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { decimalPlaces } from '../numbers/decimals';

/**
 * Rejects a number with more than `count` decimals instead of rounding it: money takes 2 (`MONEY_DECIMALS`), a
 * quantity 4 (the API limit). The error is `{ decimals: { max } }`; the message names the limit.
 */
export function maxDecimals(count: number): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return null;
    }
    return decimalPlaces(value) > count ? { decimals: { max: count } } : null;
  };
}
