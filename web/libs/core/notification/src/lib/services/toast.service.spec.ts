import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let toast: ToastService;

  const messages = () => toast.$toasts().map(item => item.message);

  beforeEach(() => {
    jest.useFakeTimers();
    TestBed.configureTestingModule({});
    toast = TestBed.inject(ToastService);
  });

  afterEach(() => jest.useRealTimers());

  it('success disappears after 4 seconds, an error stays until it is dismissed', () => {
    toast.success('Saved.');
    toast.error('It failed.');

    jest.advanceTimersByTime(3999);
    expect(messages()).toEqual(['Saved.', 'It failed.']);

    jest.advanceTimersByTime(1);
    expect(messages()).toEqual(['It failed.']);

    jest.advanceTimersByTime(60_000);
    expect(messages()).toEqual(['It failed.']);

    toast.dismiss(toast.$toasts()[0].id);
    expect(messages()).toEqual([]);
  });

  it('the same text within 2 seconds shows once; after 2 seconds it shows again', () => {
    toast.success('Saved.');
    toast.success('Saved.');
    expect(messages()).toEqual(['Saved.']);

    jest.advanceTimersByTime(2000);
    toast.success('Saved.');
    expect(messages()).toEqual(['Saved.', 'Saved.']);
  });

  it('an error repeated within 2 seconds shows once; later the new one replaces it (one node, announced again)', () => {
    toast.error('Can’t reach the server.');
    const first = toast.$toasts()[0].id;
    toast.error('Can’t reach the server.');
    expect(toast.$toasts().map(item => item.id)).toEqual([first]);

    jest.advanceTimersByTime(2000);
    toast.error('Can’t reach the server.');

    expect(messages()).toEqual(['Can’t reach the server.']);
    expect(toast.$toasts()[0].id).not.toBe(first);
  });

  it('keeps two success or info toasts at most: a newer one replaces the oldest, an error is never pushed out', () => {
    toast.success('First.');
    toast.success('Second.');
    toast.success('Third.');
    expect(messages()).toEqual(['Second.', 'Third.']);

    toast.error('Failed.');
    expect(messages()).toEqual(['Third.', 'Failed.']);

    toast.success('Fourth.');
    expect(messages()).toEqual(['Failed.', 'Fourth.']);

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
    jest.advanceTimersByTime(999);
    expect(messages()).toEqual(['Saved.']);
    jest.advanceTimersByTime(1);
    expect(messages()).toEqual([]);
  });

  it('dismissErrors removes the errors and leaves the other toasts', () => {
    toast.success('Saved.');
    toast.error('It failed.');

    toast.dismissErrors();

    expect(messages()).toEqual(['Saved.']);
  });
});
