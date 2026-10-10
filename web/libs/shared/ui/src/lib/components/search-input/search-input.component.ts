import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  computed,
  inject,
  input,
  model,
  output,
  viewChild
} from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiIconComponent } from '../icon/icon.component';

const MAX_SEARCH_LENGTH = 100;

/**
 * Search box that is ready for a keyboard-wedge barcode scanner: scanners type the code and press Enter, which emits
 * `submitted` with the exact text (docs/design/mvp-ux.md D8). Typing alone only updates `value` (debounce in the caller).
 */
@Component({
  selector: 'app-search-input',
  imports: [UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search-input.component.html',
  styleUrl: './search-input.component.scss'
})
export class UiSearchInputComponent {
  private readonly uiText = inject(UI_TEXT);

  $value = model('', { alias: 'value' });
  $label = input.required<string>({ alias: 'label' });
  $placeholder = input('', { alias: 'placeholder' });
  /** Focus the box when its dialog opens (the dialog looks for `[data-autofocus]`), so a scanner can type at once. */
  $focusOnOpen = input(false, { alias: 'focusOnOpen', transform: booleanAttribute });
  /** A pasted wall of text must not reach the API (EA-16). */
  $maxLength = input(MAX_SEARCH_LENGTH, { alias: 'maxLength' });

  $submitted = output<string>({ alias: 'submitted' });

  private readonly $control = viewChild.required<ElementRef<HTMLInputElement>>('control');

  $clearLabel = computed(() => this.uiText().clear);

  focus(): void {
    this.$control().nativeElement.focus();
  }

  onInput(event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      this.$value.set(event.target.value);
    }
  }

  onEnter(): void {
    const code = this.$value().trim();
    if (code.length > 0) {
      this.$submitted.emit(code);
    }
  }

  onClear(): void {
    this.$value.set('');
    this.focus();
  }
}
