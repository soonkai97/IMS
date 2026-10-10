import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';

@Component({
  selector: 'app-spinner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './spinner.component.html',
  styleUrl: './spinner.component.scss',
  host: { role: 'status' }
})
export class UiSpinnerComponent {
  private readonly uiText = inject(UI_TEXT);

  $label = computed(() => this.uiText().loading);
}
