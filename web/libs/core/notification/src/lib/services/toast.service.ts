import { Injectable, signal } from '@angular/core';
import type { ToastKind } from '@ims/ui';

export interface ToastAction {
  readonly label: string;
  readonly run: () => void;
}

export interface Toast {
  readonly id: number;
  readonly kind: ToastKind;
  readonly message: string;
  readonly action?: ToastAction;
  readonly createdAt: number;
}

interface DismissTimer {
  handle: ReturnType<typeof setTimeout> | undefined;
  remaining: number;
  startedAt: number;
}

const SUCCESS_TIMEOUT_MS = 4000;
const DUPLICATE_WINDOW_MS = 2000;
const MAX_VISIBLE_TOASTS = 2;
// A toast left after the pointer moved away stays long enough to read the last words.
const MIN_REMAINING_MS = 1000;

/**
 * Non-blocking feedback, and the one place a failure nobody shows in place becomes visible (error interceptor).
 *
 * - Success and info disappear after 4 s (the timer pauses while the pointer or focus is on the toast). An error stays until
 *   it is dismissed or the user starts the next action (`dismissErrors`).
 * - The same text twice within 2 s shows once. After that, an error that is still up is replaced by the new one (one node,
 *   and a screen reader hears it again: the user tried again and it failed again).
 * - At most two success or info toasts: a newer one replaces the oldest. An error is never pushed out by another toast, so
 *   errors are not capped (a burst of different failures shows each).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;

  private readonly $all = signal<readonly Toast[]>([]);
  private readonly timers = new Map<number, DismissTimer>();

  readonly $toasts = this.$all.asReadonly();

  success(message: string, action?: ToastAction): void {
    this.add('success', message, action);
  }

  info(message: string, action?: ToastAction): void {
    this.add('info', message, action);
  }

  error(message: string, action?: ToastAction): void {
    this.add('error', message, action);
  }

  dismiss(id: number): void {
    this.clearTimer(id);
    this.$all.update(toasts => toasts.filter(toast => toast.id !== id));
  }

  /** The user starts something new: what failed before is no longer news. */
  dismissErrors(): void {
    this.$all()
      .filter(toast => toast.kind === 'error')
      .forEach(toast => this.dismiss(toast.id));
  }

  /** Hover or focus on a toast: stop its timer, so a message being read does not vanish. */
  pause(id: number): void {
    const timer = this.timers.get(id);
    if (timer?.handle === undefined) {
      return;
    }
    clearTimeout(timer.handle);
    timer.handle = undefined;
    timer.remaining -= Date.now() - timer.startedAt;
  }

  resume(id: number): void {
    const timer = this.timers.get(id);
    if (timer && timer.handle === undefined) {
      this.startTimer(id, Math.max(timer.remaining, MIN_REMAINING_MS));
    }
  }

  private add(kind: ToastKind, message: string, action?: ToastAction): void {
    const now = Date.now();
    const same = this.$all().find(toast => toast.kind === kind && toast.message === message);
    if (same && now - same.createdAt < DUPLICATE_WINDOW_MS) {
      return;
    }
    if (same && kind === 'error') {
      this.dismiss(same.id);
    }
    const toast: Toast = { id: this.nextId, kind, message, action, createdAt: now };
    this.nextId += 1;
    this.$all.update(toasts => this.withRoomFor([...toasts], toast));
    if (kind !== 'error') {
      this.startTimer(toast.id, SUCCESS_TIMEOUT_MS);
    }
  }

  private withRoomFor(toasts: Toast[], added: Toast): Toast[] {
    const replaceable = toasts.filter(toast => toast.kind !== 'error');
    if (toasts.length >= MAX_VISIBLE_TOASTS && replaceable.length > 0) {
      this.clearTimer(replaceable[0].id);
      toasts.splice(toasts.indexOf(replaceable[0]), 1);
    }
    return [...toasts, added];
  }

  private startTimer(id: number, ms: number): void {
    const handle = setTimeout(() => this.dismiss(id), ms);
    this.timers.set(id, { handle, remaining: ms, startedAt: Date.now() });
  }

  private clearTimer(id: number): void {
    clearTimeout(this.timers.get(id)?.handle);
    this.timers.delete(id);
  }
}
