import { decimalPlaces } from './decimals';
import { lineAmount, roundMoney } from './money';

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

  it('rounds a negative half away from zero too (not toward +infinity)', () => {
    expect(roundMoney(-0.005)).toBe(-0.01);
    expect(roundMoney(-1.005)).toBe(-1.01);
    expect(roundMoney(-2.675)).toBe(-2.68);
    expect(roundMoney(-0.004)).toBe(0);
  });

  it('removes float noise (3 x 1.1 is 3.3, not 3.3000000000000003)', () => {
    expect(roundMoney(3 * 1.1)).toBe(3.3);
  });
});

describe('lineAmount (qty x unit cost in whole numbers, like the API)', () => {
  // Exact products: 2.5 x 0.87 = 2.175, 1.5 x 1.45 = 2.175, 9.75 x 624.18 = 6085.755, all half a cent. A floating-point
  // product gives 2.1749999..., 2.1749999... and 6085.754999..., which rounds down to the wrong cent.
  it.each([
    [2.5, 0.87, 2.18],
    [1.5, 1.45, 2.18],
    [9.75, 624.18, 6085.76],
    [3, 1.1, 3.3],
    [0.5, 0.01, 0.01],
    [0.0001, 0.5, 0],
    [4, 2.5, 10]
  ])('%s x %s is %s', (qty, cost, expected) => {
    expect(lineAmount(qty, cost)).toBe(expected);
  });

  it('rounds a negative half away from zero', () => {
    expect(lineAmount(-2.5, 0.87)).toBe(-2.18);
    expect(lineAmount(2.5, -0.87)).toBe(-2.18);
  });

  it('holds at the API limits (1,000,000,000 x 1,000,000,000.00)', () => {
    expect(lineAmount(1_000_000_000, 1_000_000_000)).toBe(1e18);
  });
});
