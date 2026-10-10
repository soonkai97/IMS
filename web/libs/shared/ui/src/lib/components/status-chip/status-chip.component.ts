import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { toClassStr } from '@ims/utils';
import { IconName } from '../icon/icon.paths';
import { UiIconComponent } from '../icon/icon.component';

export type StatusTone = 'draft' | 'submitted' | 'approved' | 'posted' | 'reversed' | 'cancelled';
export type FlagTone = 'warning' | 'danger' | 'info';

const STATUS_ICONS: Record<StatusTone, IconName> = {
  draft: 'pencil',
  submitted: 'clock',
  approved: 'check',
  posted: 'check-double',
  reversed: 'undo',
  cancelled: 'x'
};

/**
 * Document status or flag. Always icon + text + colour, never colour alone (docs/design/mvp-ux.md 7.1).
 * Use `status` for the six document statuses and `flag` for outlined badges such as "Returned".
 */
@Component({
  selector: 'app-status-chip',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './status-chip.component.html',
  styleUrl: './status-chip.component.scss',
  host: { '[class]': '$hostClass()' }
})
export class UiStatusChipComponent {
  $label = input.required<string>({ alias: 'label' });
  $status = input<StatusTone | undefined>(undefined, { alias: 'status' });
  $flag = input<FlagTone | undefined>(undefined, { alias: 'flag' });

  $icon = computed<IconName>(() => {
    const status = this.$status();
    return status ? STATUS_ICONS[status] : 'alert';
  });
  $hostClass = computed(() =>
    toClassStr([
      'ui-status-chip',
      this.$status() && `--${this.$status()}`,
      this.$flag() && `--flag-${this.$flag()}`
    ])
  );
}
