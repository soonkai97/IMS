import { computed, signal } from '@angular/core';
import { EMPTY, Observable, catchError, defer, finalize } from 'rxjs';
import { ApiOperation, IDLE_OPERATION, isFailure, isLoading } from '@ims/models';
import { trackOperation } from './track-operation';

/**
 * A write call (save, submit, post...). `run(arg)` returns a cold observable that emits the result once, records the
 * operation status, and completes empty on failure so callers never need an error callback:
 *
 *   this.store.save.run(request).pipe(tap(doc => this.open(doc)), takeUntilDestroyed(this.destroyRef)).subscribe();
 *
 * An `exclusive` command drops a call that arrives while one is in flight (it completes empty, nothing is sent): a double
 * tap on Save sends one request, not two (NR-07). The flag is set when the call starts, in the click's own tick.
 */
export class ApiCommand<Arg, Result> {
  readonly $operation = signal<ApiOperation>(IDLE_OPERATION);
  readonly $isRunning = computed(() => isLoading(this.$operation()));
  readonly $isFailed = computed(() => isFailure(this.$operation()));
  private inFlight = false;

  constructor(
    private readonly execute: (arg: Arg) => Observable<Result>,
    private readonly exclusive = false
  ) {}

  run(arg: Arg): Observable<Result> {
    const call$ = defer(() => this.execute(arg)).pipe(
      trackOperation(this.$operation),
      catchError(() => EMPTY)
    );
    if (!this.exclusive) {
      return call$;
    }
    return defer(() => {
      if (this.inFlight) {
        return EMPTY;
      }
      this.inFlight = true;
      return call$.pipe(finalize(() => (this.inFlight = false)));
    });
  }

  reset(): void {
    this.$operation.set(IDLE_OPERATION);
  }
}
