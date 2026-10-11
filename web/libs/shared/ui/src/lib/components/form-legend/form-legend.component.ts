import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';

const ASTERISK = '*';

/**
 * "* means required", placed once by a page or dialog as the first element of a form that has a required field (D4,
 * docs/design/2026-refresh-ux.md 3.3). A form with no required field leaves it out. It explains a visual symbol, so it
 * is hidden from assistive technology: every required control already says "required" on its own.
 */
@Component({
  selector: 'app-form-legend',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './form-legend.component.html',
  styleUrl: './form-legend.component.scss',
  host: { 'aria-hidden': 'true' }
})
export class UiFormLegendComponent {
  private readonly uiText = inject(UI_TEXT);

  /** The sentence starts with the asterisk in every language; it is drawn in the danger colour like the field marks. */
  $parts = computed(() => {
    const text = this.uiText().requiredLegend;
    return text.startsWith(ASTERISK)
      ? { mark: ASTERISK, rest: text.slice(ASTERISK.length).trim() }
      : { mark: '', rest: text };
  });
}
