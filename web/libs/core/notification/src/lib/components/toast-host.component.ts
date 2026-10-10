import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { UiToastComponent } from '@ims/ui';
import { Toast, ToastService } from '../services/toast.service';

/** Place once in the root component. Success toasts form a polite live region; error toasts carry role=alert. */
@Component({
  selector: 'app-toast-host',
  imports: [UiToastComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast-host.component.html',
  styleUrl: './toast-host.component.scss'
})
export class ToastHostComponent {
  private readonly toastService = inject(ToastService);

  $toasts = this.toastService.$toasts;

  onDismiss(toast: Toast): void {
    this.toastService.dismiss(toast.id);
  }

  onAction(toast: Toast): void {
    toast.action?.run();
    this.toastService.dismiss(toast.id);
  }
}
