import { Signal, computed, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl } from '@angular/forms';
import { debounceTime, startWith, tap } from 'rxjs';
import { FIRST_PAGE, SEARCH_DEBOUNCE_MS } from '@ims/models';

/** A signal that follows `source` after it stayed quiet for `ms` (type-ahead search boxes). Injection context only. */
export function debouncedSignal<Value>(
  source: Signal<Value>,
  ms: number = SEARCH_DEBOUNCE_MS
): Signal<Value> {
  return toSignal(toObservable(source).pipe(debounceTime(ms)), { initialValue: source() });
}

/** The value of a form control as a signal (follows valueChanges). Injection context only. */
export function controlSignal<Value>(control: AbstractControl<Value>): Signal<Value> {
  return toSignal(control.valueChanges.pipe(startWith(control.value)), {
    initialValue: control.value
  });
}

/**
 * Calls `load` with the current query and again whenever it changes. Pair it with a computed() query built from the
 * filter signals: the page never subscribes by hand. Injection context only (field initializer).
 */
export function loadOnChange<Query>(query: Signal<Query>, load: (query: Query) => void): void {
  toObservable(query).pipe(tap(load), takeUntilDestroyed()).subscribe();
}

export interface PageState {
  /** Current page; falls back to 1 as soon as the filters (and so `filterKey`) change. */
  readonly $page: Signal<number>;
  set(page: number): void;
}

/** Page number that resets itself when any filter or sort changes, without effects or manual resets. */
export function createPageState(filterKey: Signal<string>): PageState {
  const state = signal({ key: '', page: FIRST_PAGE });
  return {
    $page: computed(() => (state().key === filterKey() ? state().page : FIRST_PAGE)),
    set: page => state.set({ key: filterKey(), page })
  };
}
