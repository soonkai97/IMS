import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiIconComponent } from '../icon/icon.component';

/** Title row of every screen. Slots: `[chips]` next to the title and `[actions]` on the right. */
@Component({
  selector: 'app-page-header',
  imports: [RouterLink, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss'
})
export class UiPageHeaderComponent {
  protected readonly uiText = inject(UI_TEXT);

  $heading = input.required<string>({ alias: 'heading' });
  $subtitle = input<string | undefined>(undefined, { alias: 'subtitle' });
  /** Route of the parent screen; shows a back link. */
  $backLink = input<readonly unknown[] | undefined>(undefined, { alias: 'backLink' });
  $backLabel = input('', { alias: 'backLabel' });
}
