import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toClassStr } from '@ims/utils';
import { UiIconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icon.paths';

/** Big number with a label; when `link` is set the whole card is a link (dashboard shortcuts). */
@Component({
  selector: 'app-stat-card',
  imports: [RouterLink, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stat-card.component.html',
  styleUrl: './stat-card.component.scss',
  host: { '[class]': '$hostClass()' }
})
export class UiStatCardComponent {
  $label = input.required<string>({ alias: 'label' });
  $value = input.required<string>({ alias: 'value' });
  $icon = input<IconName>('info', { alias: 'icon' });
  $link = input<readonly unknown[] | undefined>(undefined, { alias: 'link' });
  $attention = input(false, { alias: 'attention' });

  $hostClass = computed(() => toClassStr(['ui-stat-card', this.$attention() && '--attention']));
}
