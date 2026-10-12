import { decimalPlaces } from './decimals';
import { roundMoney } from './money';

describe('decimalPlaces', () => {
  it('counts the written decimals', () => {
    expect(decimalPlaces(12)).toBe(0);
    expect(decimalPlaces(12.5)).toBe(1);
    expect(decimalPlaces(0.0001)).toBe(4);
    expect(decimalPlaces(0.00001)).toBe(5);
  });

  it('reads the exponent of a number JavaScript prints in scientific form', () => {
    expect(decimalPlaces(1e-7)).toBe(7);
    expect(decimalPlaces(1.25e-7)).toBe(9);
    expect(decimalPlaces(1e21)).toBe(0);
  });
});

describe('roundMoney', () => {
  it('rounds half away from zero to whole cents (backend Money.Round)', () => {
    expect(roundMoney(0.005)).toBe(0.01);
    expect(roundMoney(2.675)).toBe(2.68);
    expect(roundMoney(25.2)).toBe(25.2);
  });

  it('removes float noise (3 x 1.1 is 3.3, not 3.3000000000000003)', () => {
    expect(roundMoney(3 * 1.1)).toBe(3.3);
  });
});
