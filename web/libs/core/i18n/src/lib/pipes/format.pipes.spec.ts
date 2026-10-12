import { TestBed } from '@angular/core/testing';
import { LanguageCode } from '../constants/languages.const';
import { I18nService } from '../services/i18n.service';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  AppSignedQtyPipe,
  AppUnitPricePipe
} from './format.pipes';

describe('AppUnitPricePipe', () => {
  let pipe: AppUnitPricePipe;

  beforeEach(() => {
    pipe = TestBed.runInInjectionContext(() => new AppUnitPricePipe());
  });

  it('shows up to 4 decimals and trims trailing zeros down to 2', () => {
    expect(pipe.transform(3.3333)).toBe('3.3333');
    expect(pipe.transform(0.003)).toBe('0.003');
    expect(pipe.transform(10)).toBe('10.00');
    expect(pipe.transform(1234.5)).toBe('1,234.50');
    expect(pipe.transform(1.23456)).toBe('1.2346');
  });

  it('rounds a midpoint up, keeps a large value whole and never prints -0.00', () => {
    expect(pipe.transform(1.00005)).toBe('1.0001');
    expect(pipe.transform(1234567.8912)).toBe('1,234,567.8912');
    expect(pipe.transform(-1234.5)).toBe('-1,234.50');
    expect(pipe.transform(-0.00001)).toBe('0.00');
    expect(pipe.transform(-0)).toBe('0.00');
  });

  it('renders nothing for a missing price (no dash, no 0.00)', () => {
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform(null)).toBe('');
  });
});

describe('AppMoneyPipe', () => {
  let pipe: AppMoneyPipe;

  beforeEach(() => {
    pipe = TestBed.runInInjectionContext(() => new AppMoneyPipe());
  });

  it('always shows 2 decimals, rounds a midpoint up and keeps a large value whole', () => {
    expect(pipe.transform(2.675)).toBe('2.68');
    expect(pipe.transform(12400)).toBe('12,400.00');
    expect(pipe.transform(1000000)).toBe('1,000,000.00');
  });

  it('keeps the minus of a real negative and never prints -0.00', () => {
    expect(pipe.transform(-12.5)).toBe('-12.50');
    expect(pipe.transform(-0.004)).toBe('0.00');
    expect(pipe.transform(-0)).toBe('0.00');
  });

  it('renders nothing for a missing amount: a cost the user may not see leaves no dash and no 0.00', () => {
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform(null)).toBe('');
  });

  it('D1: rounds half away from zero, never abbreviates and never shows a sub-cent value', () => {
    expect(pipe.transform(0.005)).toBe('0.01');
    expect(pipe.transform(-0.005)).toBe('-0.01');
    expect(pipe.transform(1234567890.126)).toBe('1,234,567,890.13');
  });
});

describe.each<[LanguageCode, string]>([
  ['en', 'English'],
  ['zh', 'Chinese'],
  ['ms', 'Malay']
])('money and quantity in %s (%s)', language => {
  let money: AppMoneyPipe;
  let unitPrice: AppUnitPricePipe;
  let qty: AppQtyPipe;

  beforeEach(async () => {
    await TestBed.inject(I18nService).setLanguage(language);
    money = TestBed.runInInjectionContext(() => new AppMoneyPipe());
    unitPrice = TestBed.runInInjectionContext(() => new AppUnitPricePipe());
    qty = TestBed.runInInjectionContext(() => new AppQtyPipe());
  });

  it.each([
    [0, '0.00'],
    [12400, '12,400.00'],
    [119371.9, '119,371.90'],
    [1000000, '1,000,000.00']
  ])('shows money %s as %s', (value, expected) => {
    expect(money.transform(value)).toBe(expected);
  });

  it('shows a unit price with 2 to 4 decimals and a quantity with its own precision', () => {
    expect(unitPrice.transform(3.3333)).toBe('3.3333');
    expect(unitPrice.transform(10)).toBe('10.00');
    expect(qty.transform(1.2345)).toBe('1.2345');
    expect(qty.transform(2.5)).toBe('2.5');
    expect(qty.transform(0.0001)).toBe('0.0001');
  });
});

describe('AppSignedQtyPipe', () => {
  let pipe: AppSignedQtyPipe;

  beforeEach(() => {
    pipe = TestBed.runInInjectionContext(() => new AppSignedQtyPipe());
  });

  it('writes the sign out so the meaning does not rest on colour', () => {
    expect(pipe.transform(5)).toBe('+5');
    expect(pipe.transform(-5)).toBe('-5');
    expect(pipe.transform(-0.25)).toBe('-0.25');
  });

  it('gives zero no sign, keeps thousands readable and trims to 4 decimals', () => {
    expect(pipe.transform(0)).toBe('0');
    expect(pipe.transform(1234.5)).toBe('+1,234.5');
    expect(pipe.transform(1.23456)).toBe('+1.2346');
  });

  it('shows a dash for nothing', () => {
    expect(pipe.transform(undefined)).toBe('-');
    expect(pipe.transform(null)).toBe('-');
  });
});

describe('AppDatePipe', () => {
  let pipe: AppDatePipe;

  beforeEach(() => {
    pipe = TestBed.runInInjectionContext(() => new AppDatePipe());
  });

  it('shows a UTC timestamp (trailing Z, as the API always sends) in the business time zone', () => {
    // 16:30 UTC is 00:30 the next day in Kuala Lumpur (UTC+8).
    expect(pipe.transform('2026-10-09T16:30:00.1234567Z')).toBe('10 Oct 2026, 00:30');
  });

  it('can show the date only', () => {
    expect(pipe.transform('2026-10-09T16:30:00Z', false)).toBe('10 Oct 2026');
  });

  it('shows a dash for nothing', () => {
    expect(pipe.transform(undefined)).toBe('-');
    expect(pipe.transform(null)).toBe('-');
  });
});
