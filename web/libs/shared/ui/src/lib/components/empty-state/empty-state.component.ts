import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { UiIconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icon.paths';

/** Icon, one line, one action (put the action button inside as projected content). */
@Component({
  selector: 'app-empty-state',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './empty-state.component.html',
  styleUrl: './empty-state.component.scss'
})
export class UiEmptyStateComponent {
  $message = input.required<string>({ alias: 'message' });
  $icon = input<IconName>('info', { alias: 'icon' });
}
