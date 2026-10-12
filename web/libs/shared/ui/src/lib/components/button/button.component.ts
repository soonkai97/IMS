import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  booleanAttribute,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild
} from '@angular/core';
import { delayedLoading, toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiIconComponent } from '../icon/icon.component';
import { UiSpinnerComponent } from '../spinner/spinner.component';
import { BUTTON_SAVED_HOLD_MS, BUTTON_SPINNER_DELAY_MS } from './button.const';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'danger-outline' | 'ghost';
export type ButtonType = 'button' | 'submit';

@Component({
  selector: 'app-button',
  imports: [UiIconComponent, UiSpinnerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './button.component.html',
  styleUrl: './button.component.scss',
  host: {
    '[class]': '$hostClass()',
    '[attr.data-inert]': '$isInert() || null'
  }
})
export class UiButtonComponent {
  private readonly uiText = inject(UI_TEXT);
  private readonly control = viewChild<ElementRef<HTMLButtonElement>>('control');

  $variant = input<ButtonVariant>('secondary', { alias: 'variant' });
  $type = input<ButtonType>('button', { alias: 'type' });
  $disabled = input(false, { alias: 'disabled', transform: booleanAttribute });
  $loading = input(false, { alias: 'loading', transform: booleanAttribute });
  $block = input(false, { alias: 'block', transform: booleanAttribute });
  /** Required for icon-only buttons: there is no visible text to name the control. */
  $ariaLabel = input<string | undefined>(undefined, { alias: 'ariaLabel' });

  /**
   * For an action that finishes on the same screen (Save draft): set it to true when the save completes and the button shows
   * a tick and "Saved" for 1.5s, then returns to its label whatever the input does next.
   */
  $saved = input(false, { alias: 'saved', transform: booleanAttribute });

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

  /** `aria-busy` is at once; the spinner only after 150ms, and stays long enough to be seen. */
  $spinnerVisible = delayedLoading(this.$loading, { delay: BUTTON_SPINNER_DELAY_MS });
  $showSaved = signal(false);
  $savedText = computed(() => this.uiText().saved);
  /**
   * The width the button had when it became busy. The spinner takes the place of the leading icon, or sits in the button's
   * padding, and the box must not change size for it (no layout shift).
   */
  $lockedWidth = signal<number | null>(null);

  private savedTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const busy = this.$loading() || this.$spinnerVisible();
      untracked(() => this.lockWidth(busy));
    });
    effect(() => {
      if (this.$saved()) {
        untracked(() => this.showSaved());
      }
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.savedTimer));
  }

  onClick(event: MouseEvent): void {
    if (this.$isInert()) {
      return;
    }
    this.$clicked.emit(event);
  }

  private lockWidth(busy: boolean): void {
    if (!busy) {
      this.$lockedWidth.set(null);
    } else if (this.$lockedWidth() === null) {
      const width = this.control()?.nativeElement.offsetWidth ?? 0;
      this.$lockedWidth.set(width > 0 ? width : null);
    }
  }

  private showSaved(): void {
    clearTimeout(this.savedTimer);
    this.$showSaved.set(true);
    this.savedTimer = setTimeout(() => this.$showSaved.set(false), BUTTON_SAVED_HOLD_MS);
  }
}
