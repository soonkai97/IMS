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
}

const SUCCESS_TIMEOUT_MS = 4000;
const MAX_VISIBLE_TOASTS = 2;

/** Non-blocking feedback. Success disappears after 4 s, errors stay until dismissed (docs/design/mvp-ux.md 2). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;

  private readonly $all = signal<readonly Toast[]>([]);

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
    this.$all.update(toasts => toasts.filter(toast => toast.id !== id));
  }

  private add(kind: ToastKind, message: string, action?: ToastAction): void {
    const toast: Toast = { id: this.nextId, kind, message, action };
    this.nextId += 1;
    this.$all.update(toasts => [...toasts, toast].slice(-MAX_VISIBLE_TOASTS));
    if (kind !== 'error') {
      setTimeout(() => this.dismiss(toast.id), SUCCESS_TIMEOUT_MS);
    }
  }
}
