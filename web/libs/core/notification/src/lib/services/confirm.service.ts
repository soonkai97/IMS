import { Injectable, signal } from '@angular/core';
import { Observable, filter, map } from 'rxjs';

export interface ConfirmRequest {
  readonly heading: string;
  readonly body?: string;
  readonly confirmLabel: string;
  readonly cancelLabel: string;
  /** A third way out: the left button reads this instead of `cancelLabel` and the caller hears `alternate` (see `choose`). */
  readonly alternateLabel?: string;
  readonly destructive?: boolean;
  /** Shows a reason box with this label. */
  readonly reasonLabel?: string;
  readonly reasonRequired?: boolean;
  readonly minReasonLength?: number;
}

/** What the user did with a dialog that offers an alternative: confirmed (with the reason) or took the other way. */
export type ConfirmChoice =
  { readonly choice: 'confirm'; readonly reason: string } | { readonly choice: 'alternate' };

interface PendingConfirm {
  readonly request: ConfirmRequest;
  readonly resolve: (reason: string) => void;
  readonly alternate: () => void;
  readonly reject: () => void;
}

/**
 * Asks the user before an irreversible action. The returned observable emits the entered reason once when the user
 * confirms and completes without a value when they cancel, so callers chain with switchMap and need no flags:
 *
 *   this.confirm.ask(request).pipe(switchMap(reason => this.store.post.run(...)), takeUntilDestroyed(ref)).subscribe();
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly $current = signal<PendingConfirm | undefined>(undefined);

  readonly $pending = this.$current.asReadonly();

  ask(request: ConfirmRequest): Observable<string> {
    return this.choose(request).pipe(
      filter(
        (answer): answer is Extract<ConfirmChoice, { choice: 'confirm' }> =>
          answer.choice === 'confirm'
      ),
      map(answer => answer.reason)
    );
  }

  /**
   * Like `ask`, for a dialog with `alternateLabel`: emits `confirm` with the reason, or `alternate` when the user took the
   * other way. Closing the dialog (Esc, a tap outside, the system Back) completes without a value and changes nothing.
   */
  choose(request: ConfirmRequest): Observable<ConfirmChoice> {
    return new Observable<ConfirmChoice>(subscriber => {
      this.$current.set({
        request,
        resolve: reason => {
          subscriber.next({ choice: 'confirm', reason });
          subscriber.complete();
        },
        alternate: () => {
          subscriber.next({ choice: 'alternate' });
          subscriber.complete();
        },
        reject: () => subscriber.complete()
      });
      return () => this.$current.set(undefined);
    });
  }

  confirm(reason: string): void {
    this.$current()?.resolve(reason);
  }

  alternate(): void {
    this.$current()?.alternate();
  }

  cancel(): void {
    this.$current()?.reject();
  }
}
