import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  numberAttribute
} from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';

/** Placeholder rows shown while a list or document loads. */
@Component({
  selector: 'app-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './skeleton.component.html',
  styleUrl: './skeleton.component.scss',
  host: { role: 'status', '[attr.aria-busy]': 'true' }
})
export class UiSkeletonComponent {
  private readonly uiText = inject(UI_TEXT);

  $count = input(5, { alias: 'count', transform: numberAttribute });

  $label = computed(() => this.uiText().loading);
  $rows = computed(() => Array.from({ length: this.$count() }, (_, index) => index));
}
