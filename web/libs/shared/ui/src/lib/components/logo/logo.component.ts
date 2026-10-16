import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The product mark: three cartons on a shelf, knocked out of a rounded tile (docs/design/assets/logo-mark-mono.svg). One paint:
 * it takes the text colour of its parent (`color`), so a theme, the brand panel and forced colours need no second file. Decorative
 * (`aria-hidden`): the product name next to it is live text. Size comes from `--ui-logo-size` (default 2rem).
 */
@Component({
  selector: 'app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './logo.component.html',
  styleUrl: './logo.component.scss'
})
export class UiLogoComponent {}
