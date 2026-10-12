import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LOADING_DELAY_MS, LOADING_MIN_SHOW_MS } from '../constants';
import { delayedLoading } from './delayed-loading';

describe('delayedLoading', () => {
  let pending: WritableSignal<boolean>;
  let visible: () => boolean;

  function setPending(value: boolean): void {
    pending.set(value);
    TestBed.tick();
  }

  beforeEach(() => {
    jest.useFakeTimers();
    pending = signal(false);
    visible = TestBed.runInInjectionContext(() => delayedLoading(pending));
    TestBed.tick();
  });

  afterEach(() => jest.useRealTimers());

  it('is false before anything loads', () => {
    expect(visible()).toBe(false);
  });

  it('never shows for an answer that arrives before the delay', () => {
    setPending(true);
    jest.advanceTimersByTime(200);
    setPending(false);
    jest.advanceTimersByTime(1000);

    expect(visible()).toBe(false);
  });

  it('shows after the delay and stays for the minimum time after a late answer', () => {
    setPending(true);
    jest.advanceTimersByTime(LOADING_DELAY_MS - 1);
    expect(visible()).toBe(false);
    jest.advanceTimersByTime(1);
    expect(visible()).toBe(true);

    jest.advanceTimersByTime(50);
    setPending(false);
    jest.advanceTimersByTime(LOADING_MIN_SHOW_MS - 51);
    expect(visible()).toBe(true);
    jest.advanceTimersByTime(1);
    expect(visible()).toBe(false);
  });

  it('hides at once when the minimum time has already passed', () => {
    setPending(true);
    jest.advanceTimersByTime(LOADING_DELAY_MS + LOADING_MIN_SHOW_MS + 100);
    expect(visible()).toBe(true);

    setPending(false);
    jest.advanceTimersByTime(0);
    expect(visible()).toBe(false);
  });

  it('stays visible when a new request starts while the minimum time runs', () => {
    setPending(true);
    jest.advanceTimersByTime(LOADING_DELAY_MS);
    setPending(false);
    setPending(true);
    jest.advanceTimersByTime(LOADING_MIN_SHOW_MS + 100);

    expect(visible()).toBe(true);
  });

  it('clears its pending timer when the owner is destroyed', () => {
    setPending(true);
    expect(jest.getTimerCount()).toBe(1);

    TestBed.resetTestingModule();

    expect(jest.getTimerCount()).toBe(0);
  });

  it('takes its delay and minimum from the options', () => {
    const quick = TestBed.runInInjectionContext(() =>
      delayedLoading(pending, { delay: 10, minShow: 20 })
    );
    TestBed.tick();

    setPending(true);
    jest.advanceTimersByTime(10);
    expect(quick()).toBe(true);
    setPending(false);
    jest.advanceTimersByTime(20);
    expect(quick()).toBe(false);
  });
});
