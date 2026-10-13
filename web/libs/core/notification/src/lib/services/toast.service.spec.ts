import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let toast: ToastService;
  const messages = () => toast.$toasts().map(item => item.message);

  beforeEach(() => {
    jest.useFakeTimers();
    toast = TestBed.inject(ToastService);
  });

  afterEach(() => jest.useRealTimers());

  it('success disappears after 4 seconds, an error stays until it is dismissed or the next action', () => {
    toast.success('Saved.');
    toast.error('It failed.');

    jest.advanceTimersByTime(4000);
    jest.advanceTimersByTime(60_000);
    expect(messages()).toEqual(['It failed.']);

    toast.dismissErrors();
    expect(messages()).toEqual([]);
  });

  it('the same text within 2 seconds shows once; an error repeated later replaces the old node', () => {
    toast.success('Saved.');
    toast.success('Saved.');
    expect(messages()).toEqual(['Saved.']);

    toast.error('Offline.');
    const first = toast.$toasts()[1].id;
    toast.error('Offline.');
    expect(toast.$toasts().map(item => item.id)).toContain(first);

    jest.advanceTimersByTime(2000);
    toast.error('Offline.');
    expect(messages()).toEqual(['Saved.', 'Offline.']);
    expect(toast.$toasts().map(item => item.id)).not.toContain(first);
  });

  it('keeps two success or info toasts at most: a newer one replaces the oldest, an error is never pushed out', () => {
    toast.success('First.');
    toast.success('Second.');
    toast.success('Third.');
    toast.error('Failed.');
    expect(messages()).toEqual(['Third.', 'Failed.']);

    toast.error('Failed again.');
    toast.success('Fifth.');
    expect(messages()).toEqual(['Failed.', 'Failed again.', 'Fifth.']);
  });

  it('pauses the timer while a toast is being read, then lets it finish', () => {
    toast.success('Saved.');
    const { id } = toast.$toasts()[0];

    jest.advanceTimersByTime(3000);
    toast.pause(id);
    jest.advanceTimersByTime(60_000);
    expect(messages()).toEqual(['Saved.']);

    toast.resume(id);
    jest.advanceTimersByTime(1000);
    expect(messages()).toEqual([]);
  });
});
