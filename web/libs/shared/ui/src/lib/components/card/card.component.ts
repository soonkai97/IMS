import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Surface for a group of related content, optionally with a heading. */
@Component({
  selector: 'app-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './card.component.html',
  styleUrl: './card.component.scss'
})
export class UiCardComponent {
  $heading = input<string | undefined>(undefined, { alias: 'heading' });
}
