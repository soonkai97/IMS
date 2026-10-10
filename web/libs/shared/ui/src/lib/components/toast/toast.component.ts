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

/** One notification. Errors are `role=alert` and stay until dismissed; success and info are polite status messages. */
@Component({
  selector: 'app-toast',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast.component.html',
  styleUrl: './toast.component.scss',
  host: { '[class]': '$hostClass()', '[attr.role]': '$role()' }
})
export class UiToastComponent {
  private readonly uiText = inject(UI_TEXT);

  $kind = input<ToastKind>('info', { alias: 'kind' });
  $message = input.required<string>({ alias: 'message' });
  $actionLabel = input<string | undefined>(undefined, { alias: 'actionLabel' });

  $dismissed = output<void>({ alias: 'dismissed' });
  $actioned = output<void>({ alias: 'actioned' });

  $icon = computed(() => TOAST_ICONS[this.$kind()]);
  $role = computed(() => (this.$kind() === 'error' ? 'alert' : 'status'));
  $dismissLabel = computed(() => this.uiText().dismiss);
  $hostClass = computed(() => toClassStr(['ui-toast', `--${this.$kind()}`]));

  onDismiss(): void {
    this.$dismissed.emit();
  }

  onAction(): void {
    this.$actioned.emit();
  }
}
