import { signal } from '@angular/core';
import { createPageState } from './query-signals';

describe('createPageState', () => {
  it('starts on page 1 with 25 rows', () => {
    const state = createPageState(signal('a'));

    expect(state.$page()).toBe(1);
    expect(state.$pageSize()).toBe(25);
  });

  it('goes back to page 1 when the filters change', () => {
    const key = signal('a');
    const state = createPageState(key);
    state.set(4);

    key.set('b');

    expect(state.$page()).toBe(1);
  });

  it('a new size goes back to page 1 and stays when the filters change', () => {
    const key = signal('a');
    const state = createPageState(key);
    state.set(4);

    state.setPageSize(100);

    expect(state.$pageSize()).toBe(100);
    expect(state.$page()).toBe(1);
    state.set(2);
    key.set('b');
    expect(state.$pageSize()).toBe(100);
    expect(state.$page()).toBe(1);
  });

  it('takes the first size from the caller', () => {
    expect(createPageState(signal('a'), 50).$pageSize()).toBe(50);
  });
});
