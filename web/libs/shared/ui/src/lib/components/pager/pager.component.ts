import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiIconComponent } from '../icon/icon.component';

/** Server-side paging: "1-25 of 142" with previous / next. */
@Component({
  selector: 'app-pager',
  imports: [UiButtonComponent, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pager.component.html',
  styleUrl: './pager.component.scss'
})
export class UiPagerComponent {
  private readonly uiText = inject(UI_TEXT);

  $page = input.required<number>({ alias: 'page' });
  $pageSize = input.required<number>({ alias: 'pageSize' });
  $total = input.required<number>({ alias: 'total' });

  $pageChange = output<number>({ alias: 'pageChange' });

  $pages = computed(() => Math.max(1, Math.ceil(this.$total() / this.$pageSize())));
  $hasPrevious = computed(() => this.$page() > 1);
  $hasNext = computed(() => this.$page() < this.$pages());
  $range = computed(() => {
    const total = this.$total();
    if (total === 0) {
      return '';
    }
    const from = (this.$page() - 1) * this.$pageSize() + 1;
    const to = Math.min(total, this.$page() * this.$pageSize());
    return this.uiText().rangeStatus(from, to, total);
  });
  $pageLabel = computed(() => this.uiText().pageStatus(this.$page(), this.$pages()));
  $previousLabel = computed(() => this.uiText().previousPage);
  $nextLabel = computed(() => this.uiText().nextPage);

  onPrevious(): void {
    this.$pageChange.emit(this.$page() - 1);
  }

  onNext(): void {
    this.$pageChange.emit(this.$page() + 1);
  }
}
