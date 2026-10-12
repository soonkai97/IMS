import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiIconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icon.paths';

export type ToastKind = 'success' | 'error' | 'info';

const TOAST_ICONS: Record<ToastKind, IconName> = {
  success: 'check',
  error: 'alert',
  info: 'info'
};

/** One notification. It has no live role: app-toast-host holds the two live regions (alert and status) it appears in. */
@Component({
  selector: 'app-toast',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast.component.html',
  styleUrl: './toast.component.scss',
  host: { '[class]': '$hostClass()' }
})
export class UiToastComponent {
  private readonly uiText = inject(UI_TEXT);

  $kind = input<ToastKind>('info', { alias: 'kind' });
  $message = input.required<string>({ alias: 'message' });
  $actionLabel = input<string | undefined>(undefined, { alias: 'actionLabel' });

  $dismissed = output<void>({ alias: 'dismissed' });
  $actioned = output<void>({ alias: 'actioned' });

  $icon = computed(() => TOAST_ICONS[this.$kind()]);
  $dismissLabel = computed(() => this.uiText().dismiss);
  $hostClass = computed(() => toClassStr(['ui-toast', `--${this.$kind()}`]));

  onDismiss(): void {
    this.$dismissed.emit();
  }

  onAction(): void {
    this.$actioned.emit();
  }
}
