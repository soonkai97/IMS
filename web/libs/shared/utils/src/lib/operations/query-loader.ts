import { DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, Subject, catchError, switchMap, tap } from 'rxjs';
import { ApiOperation, IDLE_OPERATION, isFailure, isLoading } from '@ims/models';
import { trackOperation } from './track-operation';

/**
 * Latest-wins loader for a read call: `load(query)` cancels the previous request, records the operation status and
 * keeps the last successful result in `$data`. Create it in an injection context (field initializer).
 */
export class QueryLoader<Query, Result> {
  readonly $data = signal<Result | undefined>(undefined);
  readonly $operation = signal<ApiOperation>(IDLE_OPERATION);
  readonly $isLoading = computed(() => isLoading(this.$operation()));
  readonly $isFailed = computed(() => isFailure(this.$operation()));

  private readonly request$ = new Subject<Query>();
  private lastQuery: { value: Query } | undefined;

  constructor(
    private readonly fetch: (query: Query) => Observable<Result>,
    destroyRef: DestroyRef = inject(DestroyRef)
  ) {
    this.request$
      .pipe(
        switchMap(query =>
          this.fetch(query).pipe(
            trackOperation(this.$operation),
            tap(result => this.$data.set(result)),
            catchError(() => EMPTY)
          )
        ),
        takeUntilDestroyed(destroyRef)
      )
      .subscribe();
  }

  load(query: Query): void {
    this.lastQuery = { value: query };
    this.request$.next(query);
  }

  reload(): void {
    if (!this.lastQuery) {
      return;
    }
    this.request$.next(this.lastQuery.value);
  }

  reset(): void {
    this.lastQuery = undefined;
    this.$data.set(undefined);
    this.$operation.set(IDLE_OPERATION);
  }
}
