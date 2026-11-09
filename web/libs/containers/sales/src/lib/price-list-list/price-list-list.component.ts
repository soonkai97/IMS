import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PriceListListItem } from '@ims/api';
import { I18nService, MessageKey, TranslatePipe } from '@ims/core/i18n';
import { PRICE_LIST_ROUTE, PriceListQuery, PriceListsStore } from '@ims/features/sales';
import { API_MIN_SEARCH_LENGTH, PAGE_SIZES } from '@ims/models';
import {
  TableColumn,
  UiButtonComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

/**
 * Price lists: a name search, server paging, cards on phones. The default list is marked; a list shows how many prices it holds
 * and its customer, never a price (those are in the editor, for the same permission).
 */
@Component({
  selector: 'app-price-list-list-page',
  imports: [
    RouterLink,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './price-list-list.component.html',
  styleUrl: './price-list-list.component.scss'
})
export class PriceListListPageComponent {
  private readonly i18n = inject(I18nService);
  private readonly store = inject(PriceListsStore);

  readonly route = PRICE_LIST_ROUTE;
  $searchInput = signal('');
  private readonly $typed = debouncedSignal(this.$searchInput);
  /** What is sent: a search under the API minimum is no search, so 1 or 2 letters leave the list as it is. */
  private readonly $search = computed(() => {
    const text = this.$typed().trim();
    return text.length >= API_MIN_SEARCH_LENGTH ? text : '';
  });
  readonly pageSizes = PAGE_SIZES;
  private readonly pageState = createPageState(this.$search);

  readonly $query = computed<PriceListQuery>(() => ({
    search: this.$search() || undefined,
    page: this.pageState.$page(),
    pageSize: this.pageState.$pageSize()
  }));
  readonly $page = this.store.list.$data;
  readonly $isLoading = this.store.list.$isLoading;
  readonly $failed = this.store.list.$isFailed;
  readonly $columns = computed<readonly TableColumn<PriceListListItem>[]>(() => [
    {
      key: 'name',
      header: this.i18n.t('pricelist.name'),
      value: row => row.name,
      primary: true
    },
    { key: 'customer', header: this.i18n.t('pricelist.customer'), phoneInline: true },
    {
      key: 'count',
      header: this.i18n.t('pricelist.col.count'),
      align: 'end',
      phoneInline: true
    },
    { key: 'flags', type: 'status', header: this.i18n.t('product.status'), phoneEnd: true }
  ]);
  readonly $emptyKey = computed<MessageKey>(() =>
    this.$search() === '' ? 'pricelist.empty' : 'pricelist.noMatch'
  );
  readonly rowLink = (row: PriceListListItem): readonly unknown[] => [
    '/',
    PRICE_LIST_ROUTE,
    row.id
  ];

  constructor() {
    // The store outlives a sign-out: the next user must not see the last user's lists for a moment.
    this.store.list.reset();
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageState.setPageSize(pageSize);
  }

  onRetry(): void {
    this.store.list.reload();
  }
}
