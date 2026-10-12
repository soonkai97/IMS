import { FormControl } from '@angular/forms';
import { MONEY_DECIMALS } from '../constants';
import { maxDecimals } from './decimals.validator';

describe('maxDecimals (R04: money has 2 decimals, never sub-cent)', () => {
  const money = maxDecimals(MONEY_DECIMALS);
  const check = (value: unknown) => money(new FormControl(value));

  it.each([10, 10.5, 0.01, 12400, 0, 1e21])('accepts %s', value => {
    expect(check(value)).toBeNull();
  });

  it('blocks a 3rd decimal, typed or pasted, and says the limit', () => {
    expect(check(12.345)).toEqual({ decimals: { max: 2 } });
    expect(check(12.3456)).toEqual({ decimals: { max: 2 } });
    expect(check(0.001)).toEqual({ decimals: { max: 2 } });
  });

  it('blocks a sub-cent amount that JavaScript prints with an exponent', () => {
    expect(check(1e-7)).toEqual({ decimals: { max: 2 } });
    expect(check(1.5e-7)).toEqual({ decimals: { max: 2 } });
  });

  it('judges a negative like a positive (the minimum rule reports the sign)', () => {
    expect(check(-12.34)).toBeNull();
    expect(check(-12.345)).toEqual({ decimals: { max: 2 } });
  });

  it('leaves empty, text and non-finite values to the other validators', () => {
    expect(check(null)).toBeNull();
    expect(check('')).toBeNull();
    expect(check('abc')).toBeNull();
    expect(check(Number.NaN)).toBeNull();
    expect(check(Number.POSITIVE_INFINITY)).toBeNull();
  });

  it('takes any limit (a quantity allows 4)', () => {
    const quantity = maxDecimals(4);

    expect(quantity(new FormControl(1.2345))).toBeNull();
    expect(quantity(new FormControl(0.00001))).toEqual({ decimals: { max: 4 } });
  });
});
