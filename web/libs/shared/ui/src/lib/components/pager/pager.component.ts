import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiIconComponent } from '../icon/icon.component';

let nextPagerId = 0;

/** Where the focus goes after a page change: the table above the pager (its caption). */
export interface PagerFocusTarget {
  focusCaption(): void;
}

/**
 * Server-side paging: "1-25 of 142" with previous / next. A pager whose container is wide (a desktop) also has First and Last
 * and, when `pageSizes` is given, a "Rows per page" select; a phone keeps the range and the two arrows. A page change moves
 * the focus to `table` (the pressed button may be disabled by the new page) and is announced once, as "Page 3 of 14, 51-75 of 333".
 */
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
  /** The choices of the "Rows per page" select. None (the default) = no select. */
  $pageSizes = input<readonly number[]>([], { alias: 'pageSizes' });
  /** The table this pager pages: it takes the focus after a change. */
  $table = input<PagerFocusTarget | undefined>(undefined, { alias: 'table' });

  $pageChange = output<number>({ alias: 'pageChange' });
  $pageSizeChange = output<number>({ alias: 'pageSizeChange' });

  readonly sizeId = `ui-pager-size-${nextPagerId++}`;

  $text = computed(() => this.uiText());
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
  /** The one polite announcement of a change. */
  $announcement = computed(() => [this.$pageLabel(), this.$range()].filter(Boolean).join(', '));

  onFirst(): void {
    this.goTo(1);
  }

  onPrevious(): void {
    this.goTo(this.$page() - 1);
  }

  onNext(): void {
    this.goTo(this.$page() + 1);
  }

  onLast(): void {
    this.goTo(this.$pages());
  }

  onPageSize(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }
    this.$pageSizeChange.emit(Number(event.target.value));
    this.$table()?.focusCaption();
  }

  private goTo(page: number): void {
    this.$pageChange.emit(page);
    this.$table()?.focusCaption();
  }
}
