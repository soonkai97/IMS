import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ICON_PATHS, IconName } from './icon.paths';

/** The only icon sizes in px (visual spec 2.9): inside chips and text, buttons and fields, navigation, empty state. */
export type IconSize = 16 | 20 | 24 | 40;

/** Stroke in screen pixels (the path uses a non-scaling stroke): thinner at 16px, which looked too faint at 1.3px. */
const SMALL_STROKE = 1.5;
const STROKE = 1.75;

/** Decorative icon (aria-hidden). Put the accessible name on the surrounding button or text. */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon.component.html',
  styleUrl: './icon.component.scss',
  host: { '[attr.data-size]': '$size()' }
})
export class UiIconComponent {
  $name = input.required<IconName>({ alias: 'name' });
  $size = input<IconSize>(24, { alias: 'size' });

  $path = computed(() => ICON_PATHS[this.$name()]);
  $stroke = computed(() => (this.$size() === 16 ? SMALL_STROKE : STROKE));
}
