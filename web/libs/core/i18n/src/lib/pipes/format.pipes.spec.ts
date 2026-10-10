import { TestBed } from '@angular/core/testing';
import { AppDatePipe } from './format.pipes';

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
