const EXPONENT_MARK = 'e';

/**
 * Number of decimals a number is written with: 12.5 -> 1, 12 -> 0. JavaScript prints 0.0000001 as "1e-7", so the
 * exponent is read too (a plain split on "." would call that 0 decimals and let a sub-cent amount through).
 */
export function decimalPlaces(value: number): number {
  const [mantissa, exponent] = String(value).toLowerCase().split(EXPONENT_MARK);
  const fraction = (mantissa.split('.')[1] ?? '').length;
  return Math.max(0, fraction - Number(exponent ?? 0));
}
