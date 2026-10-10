import { computed, signal } from '@angular/core';
import { EMPTY, Observable, catchError, defer } from 'rxjs';
import { ApiOperation, IDLE_OPERATION, isFailure, isLoading } from '@ims/models';
import { trackOperation } from './track-operation';

/**
 * A write call (save, submit, post...). `run(arg)` returns a cold observable that emits the result once, records the
 * operation status, and completes empty on failure so callers never need an error callback:
 *
 *   this.store.save.run(request).pipe(tap(doc => this.open(doc)), takeUntilDestroyed(this.destroyRef)).subscribe();
 */
export class ApiCommand<Arg, Result> {
  readonly $operation = signal<ApiOperation>(IDLE_OPERATION);
  readonly $isRunning = computed(() => isLoading(this.$operation()));
  readonly $isFailed = computed(() => isFailure(this.$operation()));

  constructor(private readonly execute: (arg: Arg) => Observable<Result>) {}

  run(arg: Arg): Observable<Result> {
    return defer(() => this.execute(arg)).pipe(
      trackOperation(this.$operation),
      catchError(() => EMPTY)
    );
  }

  reset(): void {
    this.$operation.set(IDLE_OPERATION);
  }
}
