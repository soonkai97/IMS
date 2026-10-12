import { Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, firstValueFrom, of, throwError, toArray } from 'rxjs';
import { API_STATUS, ApiError } from '@ims/models';
import { ApiCommand } from './api-command';
import { QueryLoader } from './query-loader';

const API_ERROR: ApiError = {
  isApiError: true,
  kind: 'conflict',
  status: 409,
  title: 'Conflict',
  detail: 'Stock moved.',
  fieldErrors: {},
  extensions: {}
};

describe('ApiCommand', () => {
  it('records loading then success and emits the result once', async () => {
    const gate = new Subject<number>();
    const command = new ApiCommand<void, number>(() => gate);
    const values: number[] = [];

    command.run().subscribe(value => values.push(value));
    expect(command.$operation().status).toBe(API_STATUS.Loading);
    expect(command.$isRunning()).toBe(true);

    gate.next(7);
    gate.complete();

    expect(values).toEqual([7]);
    expect(command.$operation().status).toBe(API_STATUS.Success);
  });

  it('records the failure and completes empty, so callers need no error callback', async () => {
    const command = new ApiCommand<void, number>(() => throwError(() => API_ERROR));

    const emitted = await firstValueFrom(command.run().pipe(toArray()));

    expect(emitted).toEqual([]);
    expect(command.$operation().status).toBe(API_STATUS.Failure);
    expect(command.$operation().error?.detail).toBe('Stock moved.');
  });

  describe('exclusive (NR-07)', () => {
    it('sends one request for two calls in the same tick and for a second call while the first runs', () => {
      const gate = new Subject<number>();
      const execute = jest.fn(() => gate);
      const command = new ApiCommand<void, number>(execute, true);
      const values: number[] = [];

      command.run().subscribe(value => values.push(value));
      command.run().subscribe(value => values.push(value));
      command.run().subscribe(value => values.push(value));
      gate.next(1);
      gate.complete();

      expect(execute).toHaveBeenCalledTimes(1);
      expect(values).toEqual([1]);
    });

    it('accepts the next call once the first finished, failed, or was unsubscribed', () => {
      const execute = jest
        .fn<Observable<number>, []>()
        .mockReturnValueOnce(throwError(() => API_ERROR))
        .mockReturnValueOnce(new Subject<number>())
        .mockReturnValue(of(3));
      const command = new ApiCommand<void, number>(execute, true);

      command.run().subscribe();
      const abandoned = command.run().subscribe();
      abandoned.unsubscribe();
      command.run().subscribe();

      expect(execute).toHaveBeenCalledTimes(3);
    });

    it('a command that is not exclusive still runs every call', () => {
      const execute = jest.fn(() => new Subject<number>());
      const command = new ApiCommand<void, number>(execute);

      command.run().subscribe();
      command.run().subscribe();

      expect(execute).toHaveBeenCalledTimes(2);
    });
  });
});

describe('QueryLoader', () => {
  function create<Result>(fetch: (query: string) => Observable<Result>) {
    return runInInjectionContext(
      TestBed.inject(Injector),
      () => new QueryLoader<string, Result>(fetch)
    );
  }

  it('keeps the latest result and supports reload with the last query', () => {
    const fetch = jest.fn((query: string) => of(`result of ${query}`));
    const loader = create<string>(fetch);

    loader.load('a');
    loader.load('b');
    loader.reload();

    expect(loader.$data()).toBe('result of b');
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(fetch).toHaveBeenLastCalledWith('b');
  });

  it('latest wins: a slow earlier request never overwrites a newer one', () => {
    const slow = new Subject<string>();
    const fast = new Subject<string>();
    const loader = create<string>(query => (query === 'slow' ? slow : fast));

    loader.load('slow');
    loader.load('fast');
    fast.next('fast result');
    fast.complete();
    slow.next('slow result');
    slow.complete();

    expect(loader.$data()).toBe('fast result');
  });

  it('exposes failures through the operation signal', () => {
    const loader = create<string>(() => throwError(() => API_ERROR));

    loader.load('x');

    expect(loader.$isFailed()).toBe(true);
    expect(loader.$data()).toBeUndefined();
  });
});
