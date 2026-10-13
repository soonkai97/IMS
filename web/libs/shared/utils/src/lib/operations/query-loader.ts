import { HttpContext } from '@angular/common/http';
import { DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, Subject, catchError, switchMap, tap } from 'rxjs';
import { ApiOperation, IDLE_OPERATION, isFailure, isLoading } from '@ims/models';
import { trackOperation } from './track-operation';

interface LoadRequest<Query> {
  readonly query: Query;
  readonly context?: HttpContext;
}

/**
 * Latest-wins loader for a read call: `load(query)` cancels the previous request, records the operation status and
 * keeps the last successful result in `$data`. Create it in an injection context (field initializer).
 *
 * `load(query, context)` hands an HttpContext to the call (`fetch` forwards it to the generated client): the screen that shows
 * a failure in place passes `handledInline()`, so no toast repeats it. `reload()` repeats the last query with its context.
 */
export class QueryLoader<Query, Result> {
  readonly $data = signal<Result | undefined>(undefined);
  readonly $operation = signal<ApiOperation>(IDLE_OPERATION);
  readonly $isLoading = computed(() => isLoading(this.$operation()));
  readonly $isFailed = computed(() => isFailure(this.$operation()));

  private readonly request$ = new Subject<LoadRequest<Query>>();
  private lastRequest: LoadRequest<Query> | undefined;

  constructor(
    private readonly fetch: (query: Query, context?: HttpContext) => Observable<Result>,
    destroyRef: DestroyRef = inject(DestroyRef)
  ) {
    this.request$
      .pipe(
        switchMap(({ query, context }) =>
          this.fetch(query, context).pipe(
            trackOperation(this.$operation),
            tap(result => this.$data.set(result)),
            catchError(() => EMPTY)
          )
        ),
        takeUntilDestroyed(destroyRef)
      )
      .subscribe();
  }

  load(query: Query, context?: HttpContext): void {
    this.lastRequest = { query, context };
    this.request$.next(this.lastRequest);
  }

  reload(): void {
    if (!this.lastRequest) {
      return;
    }
    this.request$.next(this.lastRequest);
  }

  reset(): void {
    this.lastRequest = undefined;
    this.$data.set(undefined);
    this.$operation.set(IDLE_OPERATION);
  }
}
