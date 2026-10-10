import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { UiConfirmDialogComponent } from '@ims/ui';
import { ConfirmService } from '../services/confirm.service';

/** Renders the dialog for whatever ConfirmService.ask() is waiting on. Place once in the root component. */
@Component({
  selector: 'app-confirm-host',
  imports: [UiConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-host.component.html'
})
export class ConfirmHostComponent {
  private readonly confirmService = inject(ConfirmService);

  $request = computed(() => this.confirmService.$pending()?.request);
  $isOpen = computed(() => this.$request() !== undefined);

  onConfirmed(reason: string): void {
    this.confirmService.confirm(reason);
  }

  onCancelled(): void {
    this.confirmService.cancel();
  }
}
