import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output
} from '@angular/core';
import { toClassStr } from '@ims/utils';
import { UiSpinnerComponent } from '../spinner/spinner.component';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonType = 'button' | 'submit';

@Component({
  selector: 'app-button',
  imports: [UiSpinnerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './button.component.html',
  styleUrl: './button.component.scss',
  host: {
    '[class]': '$hostClass()',
    '[attr.data-inert]': '$isInert() || null'
  }
})
export class UiButtonComponent {
  $variant = input<ButtonVariant>('secondary', { alias: 'variant' });
  $type = input<ButtonType>('button', { alias: 'type' });
  $disabled = input(false, { alias: 'disabled', transform: booleanAttribute });
  $loading = input(false, { alias: 'loading', transform: booleanAttribute });
  $block = input(false, { alias: 'block', transform: booleanAttribute });
  /** Required for icon-only buttons: there is no visible text to name the control. */
  $ariaLabel = input<string | undefined>(undefined, { alias: 'ariaLabel' });

  /** For a button that shows or hides something (a filter panel): true/false is read out as expanded/collapsed. */
  $expanded = input<boolean | undefined>(undefined, { alias: 'expanded' });
  $controls = input<string | undefined>(undefined, { alias: 'controls' });
  /** Opens a dialog: assistive technology announces it ("has popup dialog"). */
  $popup = input<'dialog' | undefined>(undefined, { alias: 'popup' });

  /**
   * Looks and acts disabled but stays focusable and announced: a keyboard or screen-reader user can reach it and
   * learn why from `describedBy` (a native disabled button is skipped by Tab).
   */
  $ariaDisabled = input(false, { alias: 'ariaDisabled', transform: booleanAttribute });
  $describedBy = input<string | undefined>(undefined, { alias: 'describedBy' });

  $clicked = output<MouseEvent>({ alias: 'clicked' });

  /**
   * A button that is busy keeps the focus (a natively disabled one drops it, and a keyboard user is thrown back to the
   * top of the page): it is only inert and announced as busy. Only `disabled` switches the control off natively.
   */
  $isNativeDisabled = computed(() => this.$disabled());
  $isInert = computed(() => this.$disabled() || this.$loading() || this.$ariaDisabled());
  $isAriaDisabled = computed(() => this.$ariaDisabled() || this.$loading());
  $hostClass = computed(() =>
    toClassStr(['ui-button', `--${this.$variant()}`, this.$block() && '--block'])
  );

  onClick(event: MouseEvent): void {
    if (this.$isInert()) {
      return;
    }
    this.$clicked.emit(event);
  }
}
