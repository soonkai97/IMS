import { TestBed } from '@angular/core/testing';
import { AppDatePipe, AppSignedQtyPipe } from './format.pipes';

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
