import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';
import { UiToastComponent } from '@ims/ui';
import { Toast, ToastService } from '../services/toast.service';

/**
 * Place once in the root component. It never takes focus. Errors and messages sit in two live regions that are always
 * present (see the template); an error toast goes when the user moves on to another page.
 */
@Component({
  selector: 'app-toast-host',
  imports: [NgTemplateOutlet, UiToastComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast-host.component.html',
  styleUrl: './toast-host.component.scss'
})
export class ToastHostComponent {
  private readonly toastService = inject(ToastService);

  $errors = computed(() => this.toastService.$toasts().filter(toast => toast.kind === 'error'));
  $messages = computed(() => this.toastService.$toasts().filter(toast => toast.kind !== 'error'));

  constructor() {
    inject(Router)
      .events.pipe(
        filter(event => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef))
      )
      .subscribe(() => this.toastService.dismissErrors());
  }

  onDismiss(toast: Toast): void {
    this.toastService.dismiss(toast.id);
  }

  onAction(toast: Toast): void {
    toast.action?.run();
    this.toastService.dismiss(toast.id);
  }

  /** The pointer or focus is on a toast: it is being read, so its timer stops. */
  onHold(toast: Toast): void {
    this.toastService.pause(toast.id);
  }

  /** The timer runs again only when neither the pointer nor focus is left on the toast. */
  onRelease(toast: Toast, event: Event): void {
    const element = event.currentTarget as HTMLElement;
    if (!element.matches(':hover') && !element.contains(element.ownerDocument.activeElement)) {
      this.toastService.resume(toast.id);
    }
  }
}
