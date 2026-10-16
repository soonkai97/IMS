import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input
} from '@angular/core';
import { toClassStr } from '@ims/utils';
import { IconName } from '../icon/icon.paths';
import { UiIconComponent } from '../icon/icon.component';

export type BannerKind = 'info' | 'warning' | 'danger' | 'success';

const BANNER_ICONS: Record<BannerKind, IconName> = {
  info: 'info',
  warning: 'alert',
  danger: 'alert',
  success: 'check'
};

/** Inline message block. Danger is an alert, the other kinds are polite status messages; `alert` makes any kind an alert. */
@Component({
  selector: 'app-banner',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './banner.component.html',
  styleUrl: './banner.component.scss',
  host: { '[class]': '$hostClass()', '[attr.role]': '$role()', '[attr.tabindex]': '$tabindex()' }
})
export class UiBannerComponent {
  $kind = input<BannerKind>('info', { alias: 'kind' });
  $heading = input<string | undefined>(undefined, { alias: 'heading' });
  $alert = input(false, { alias: 'alert', transform: booleanAttribute });

  $icon = computed(() => BANNER_ICONS[this.$kind()]);
  $isAlert = computed(() => this.$kind() === 'danger' || this.$alert());
  $role = computed(() => (this.$isAlert() ? 'alert' : 'status'));
  /** An error can receive focus from code (not from Tab), so a keyboard user lands on it after a failed action. */
  $tabindex = computed(() => (this.$isAlert() ? -1 : null));
  $hostClass = computed(() => toClassStr(['ui-banner', `--${this.$kind()}`]));
}
