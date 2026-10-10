import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ICON_PATHS, IconName } from './icon.paths';

/** Decorative icon (aria-hidden). Put the accessible name on the surrounding button or text. */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon.component.html',
  styleUrl: './icon.component.scss'
})
export class UiIconComponent {
  $name = input.required<IconName>({ alias: 'name' });

  $path = computed(() => ICON_PATHS[this.$name()]);
}
