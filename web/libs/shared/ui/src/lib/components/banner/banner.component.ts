import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
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

/** Inline message block. Danger is announced immediately (role=alert); the others politely (role=status). */
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

  $icon = computed(() => BANNER_ICONS[this.$kind()]);
  $role = computed(() => (this.$kind() === 'danger' ? 'alert' : 'status'));
  /** An error can receive focus from code (not from Tab), so a keyboard user lands on it after a failed action. */
  $tabindex = computed(() => (this.$kind() === 'danger' ? -1 : null));
  $hostClass = computed(() => toClassStr(['ui-banner', `--${this.$kind()}`]));
}
