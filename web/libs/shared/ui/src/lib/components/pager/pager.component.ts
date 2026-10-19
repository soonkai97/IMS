import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output
} from '@angular/core';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiIconComponent } from '../icon/icon.component';

let nextPagerId = 0;

/**
 * Server-side paging: "1-25 of 142" with previous / next. A pager whose container is wide (a desktop) also has First and Last
 * and, when `pageSizes` is given, a "Rows per page" select; a phone keeps the range and the two arrows. A change is announced
 * once, as "Page 3 of 14, 51-75 of 333". The focus never moves: a button the new page cannot use (Last on the last page) is
 * aria-disabled, not disabled, so it keeps the focus and a keyboard user pages on from where they are. A change also brings the
 * top of the table above the pager back into view when it was scrolled away (a phone page of cards is long): the table is
 * scrolled to the top of the window at once, never focused.
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  $page = input.required<number>({ alias: 'page' });
  $pageSize = input.required<number>({ alias: 'pageSize' });
  $total = input.required<number>({ alias: 'total' });
  /** The choices of the "Rows per page" select. None (the default) = no select. */
  $pageSizes = input<readonly number[]>([], { alias: 'pageSizes' });

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
  $announcement = computed(() => {
    const range = this.$range();
    return range ? this.uiText().pageAnnouncement(this.$pageLabel(), range) : this.$pageLabel();
  });

  onFirst(): void {
    this.changePage(1);
  }

  onPrevious(): void {
    this.changePage(this.$page() - 1);
  }

  onNext(): void {
    this.changePage(this.$page() + 1);
  }

  onLast(): void {
    this.changePage(this.$pages());
  }

  onPageSize(event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) {
      return;
    }
    this.$pageSizeChange.emit(Number(event.target.value));
    this.revealTable();
  }

  private changePage(page: number): void {
    this.$pageChange.emit(page);
    this.revealTable();
  }

  /**
   * The table is the closest `app-table` before this pager. When its top is scrolled out of the window (or under the top bar,
   * its `scroll-margin-top`), it is scrolled to the top of the window, so the new page starts at its first row.
   */
  private revealTable(): void {
    let table = this.host.nativeElement.previousElementSibling;
    while (table && table.tagName !== 'APP-TABLE') {
      table = table.previousElementSibling;
    }
    if (!table) {
      return;
    }
    const margin = parseFloat(getComputedStyle(table).scrollMarginTop) || 0;
    if (table.getBoundingClientRect().top < margin) {
      table.scrollIntoView?.({ block: 'start', behavior: 'instant' });
    }
  }
}
