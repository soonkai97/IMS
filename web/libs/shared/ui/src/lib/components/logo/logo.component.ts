import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The product mark (docs/design/assets/logo-mark-mono.svg): one paint, the text colour of its parent, so themes and forced colours
 * need no second file. Decorative (`aria-hidden`): the name next to it is live text. Size: `--ui-logo-size` (default 2rem).
 */
@Component({
  selector: 'app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './logo.component.html',
  styleUrl: './logo.component.scss'
})
export class UiLogoComponent {}
