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

  $clicked = output<MouseEvent>({ alias: 'clicked' });

  $isInert = computed(() => this.$disabled() || this.$loading());
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
