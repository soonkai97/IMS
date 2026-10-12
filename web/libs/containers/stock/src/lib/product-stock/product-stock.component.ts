import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input } from '@angular/core';
import { LedgerRow } from '@ims/api';
import {
  AppDatePipe,
  AppQtyPipe,
  I18nService,
  LEDGER_REASON_LABELS,
  TranslatePipe
} from '@ims/core/i18n';
import { ProductsStore } from '@ims/features/catalog';
import { routeForDocumentNo } from '@ims/features/documents';
import { LedgerQuery, StockStore } from '@ims/features/stock';
import { DEFAULT_PAGE_SIZE } from '@ims/models';
import {
  TableColumn,
  UiCardComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiTableComponent
} from '@ims/ui';
import { createPageState, loadOnChange, normalizePublicId } from '@ims/utils';

/** Stock of one product per warehouse, plus its append-only ledger; every line links to the source document (STK-02). */
@Component({
  selector: 'app-product-stock-page',
  imports: [
    AppDatePipe,
    AppQtyPipe,
    TranslatePipe,
    UiCardComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-stock.component.html',
  styleUrl: './product-stock.component.scss'
})
export class ProductStockPageComponent implements OnInit {
  private readonly i18n = inject(I18nService);
  private readonly products = inject(ProductsStore);
  private readonly store = inject(StockStore);

  /** Route parameter :productId */
  $productId = input.required<string>({ alias: 'productId' });

  /** The id as the API writes it; undefined for an old bookmark (`/stock/123`), which is a not-found page, no request. */
  private readonly $id = computed(() => normalizePublicId(this.$productId()));
  readonly $notFound = computed(() => this.$id() === undefined);
  private readonly pageState = createPageState(this.$productId);

  readonly $query = computed<LedgerQuery>(() => ({
    productId: this.$id(),
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $product = this.products.detail.$data;
  $stockRows = computed(() => this.store.productStock.$data()?.items ?? []);
  $ledger = this.store.ledger.$data;
  $isLoading = this.store.ledger.$isLoading;
  $failed = this.store.ledger.$isFailed;
  $columns = computed<readonly TableColumn<LedgerRow>[]>(() => [
    {
      key: 'document',
      type: 'code',
      header: this.i18n.t('ledger.document'),
      value: row => row.sourceDocNo,
      primary: true
    },
    { key: 'date', type: 'date', header: this.i18n.t('ledger.date') },
    {
      key: 'reason',
      header: this.i18n.t('ledger.reason'),
      value: row => this.i18n.enumLabel(LEDGER_REASON_LABELS, row.reason)
    },
    { key: 'warehouse', header: this.i18n.t('stock.warehouse'), value: row => row.warehouseName },
    { key: 'qty', header: this.i18n.t('ledger.qtyChange'), align: 'end' },
    { key: 'user', header: this.i18n.t('ledger.user'), value: row => row.user, hideOnPhone: true }
  ]);

  readonly rowLink = (row: LedgerRow): readonly unknown[] => [
    '/',
    routeForDocumentNo(row.sourceDocNo),
    row.sourceDocId
  ];

  constructor() {
    loadOnChange(this.$query, query => {
      if (query.productId !== undefined) {
        this.store.ledger.load(query);
      }
    });
  }

  ngOnInit(): void {
    const id = this.$id();
    if (id !== undefined) {
      this.products.detail.load(id);
      this.store.productStock.load(id);
    }
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onRetry(): void {
    this.store.ledger.reload();
  }
}
