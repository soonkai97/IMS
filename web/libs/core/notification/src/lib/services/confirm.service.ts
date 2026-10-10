import { Injectable, signal } from '@angular/core';
import { Observable } from 'rxjs';

export interface ConfirmRequest {
  readonly heading: string;
  readonly body?: string;
  readonly confirmLabel: string;
  readonly cancelLabel: string;
  readonly destructive?: boolean;
  /** Shows a reason box with this label. */
  readonly reasonLabel?: string;
  readonly reasonRequired?: boolean;
  readonly minReasonLength?: number;
}

interface PendingConfirm {
  readonly request: ConfirmRequest;
  readonly resolve: (reason: string) => void;
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
    return new Observable<string>(subscriber => {
      this.$current.set({
        request,
        resolve: reason => {
          subscriber.next(reason);
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

  cancel(): void {
    this.$current()?.reject();
  }
}
