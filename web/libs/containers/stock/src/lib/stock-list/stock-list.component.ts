import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { StockRow, StockSort } from '@ims/api';
import { AppMoneyPipe, AppQtyPipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { CatalogStore } from '@ims/features/catalog';
import { ReportsStore } from '@ims/features/reports';
import { StockQuery, StockStore } from '@ims/features/stock';
import { WarehousesStore } from '@ims/features/warehousing';
import { PAGE_SIZES, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  TableColumn,
  columnWhen,
  TableSort,
  UiButtonComponent,
  UiCellDirective,
  UiCheckboxComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiSelectComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { controlSignal, createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

const SORT_KEYS: Readonly<Record<string, StockSort>> = {
  Sku: StockSort.Sku,
  QtyOnHand: StockSort.QtyOnHand,
  Value: StockSort.Value,
  Warehouse: StockSort.Warehouse
};

/** Stock enquiry: what is on hand, per product and warehouse (STK-01). */
@Component({
  selector: 'app-stock-list-page',
  imports: [
    AppMoneyPipe,
    AppQtyPipe,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiCheckboxComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiSelectComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-list.component.html',
  styleUrl: './stock-list.component.scss'
})
export class StockListPageComponent implements OnInit {
  private readonly catalog = inject(CatalogStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly reports = inject(ReportsStore);
  private readonly store = inject(StockStore);
  private readonly warehouses = inject(WarehousesStore);

  readonly warehouseControl = new FormControl<PublicId | null>(null);
  readonly categoryControl = new FormControl<PublicId | null>(null);
  readonly lowOnlyControl = new FormControl(false, { nonNullable: true });

  $searchInput = signal('');
  $sort = signal<TableSort | undefined>(undefined);

  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $warehouseId = controlSignal(this.warehouseControl);
  private readonly $categoryId = controlSignal(this.categoryControl);
  private readonly $lowOnly = controlSignal(this.lowOnlyControl);
  private readonly $filterKey = computed(() =>
    JSON.stringify([
      this.$search(),
      this.$warehouseId(),
      this.$categoryId(),
      this.$lowOnly(),
      this.$sort()
    ])
  );
  readonly pageSizes = PAGE_SIZES;
  private readonly pageState = createPageState(this.$filterKey);

  readonly $query = computed<StockQuery>(() => ({
    search: this.$search() || undefined,
    warehouseId: this.$warehouseId() ?? undefined,
    categoryId: this.$categoryId() ?? undefined,
    lowOnly: this.$lowOnly() || undefined,
    page: this.pageState.$page(),
    pageSize: this.pageState.$pageSize(),
    sortBy: this.sortKey(this.$sort()),
    desc: this.$sort()?.desc
  }));

  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $isExporting = this.reports.exportStockSummary.$isRunning;
  $canExport = computed(() => this.permissions.has(PERMISSION.ReportsRead));
  $canCreate = computed(() => this.permissions.has(PERMISSION.DocumentCreate));
  private readonly $canViewValuation = computed(() =>
    this.permissions.has(PERMISSION.ValuationView)
  );
  $showWarehouseFilter = computed(() => this.warehouses.$activeWarehouses().length > 1);
  $hasFilters = computed(
    () => this.$search().length > 0 || this.$lowOnly() || this.$categoryId() !== null
  );
  $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );
  $categoryOptions = computed<readonly SelectOption[]>(() =>
    this.catalog.$categories().map(category => ({ value: category.id, label: category.name }))
  );
  $columns = computed<readonly TableColumn<StockRow>[]>(() => [
    {
      key: 'sku',
      type: 'code',
      header: this.i18n.t('product.sku'),
      value: row => row.sku,
      primary: true,
      sortKey: StockSort.Sku
    },
    {
      key: 'name',
      header: this.i18n.t('product.name'),
      value: row => row.productName,
      phoneInline: true
    },
    {
      key: 'category',
      header: this.i18n.t('product.category'),
      value: row => row.categoryName ?? '',
      hideOnPhone: true
    },
    {
      key: 'warehouse',
      header: this.i18n.t('stock.warehouse'),
      value: row => row.warehouseName ?? '',
      phoneInline: true,
      sortKey: StockSort.Warehouse
    },
    {
      key: 'onHand',
      header: this.i18n.t('stock.onHand'),
      align: 'end',
      phoneInline: true,
      sortKey: StockSort.QtyOnHand
    },
    { key: 'safety', header: this.i18n.t('product.safetyStock'), align: 'end', hideOnPhone: true },
    // Value and sorting by it need valuation.view; the server answers 400 to sortBy=Value without it (mvp-ux 1.4).
    ...columnWhen<StockRow>(this.$canViewValuation(), {
      key: 'value',
      header: this.i18n.t('stock.value'),
      align: 'end',
      hideOnPhone: true,
      sortKey: StockSort.Value
    }),
    { key: 'status', type: 'status', header: this.i18n.t('product.status'), phoneEnd: true }
  ]);

  readonly rowLink = (row: StockRow): readonly unknown[] => ['/stock', row.productId];

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  ngOnInit(): void {
    this.catalog.ensureLoaded();
    this.warehouses.ensureLoaded();
  }

  /** Never ask for the value order without the right to see values: the API refuses it with 400. */
  private sortKey(sort: TableSort | undefined): StockSort | undefined {
    const key = SORT_KEYS[sort?.sortBy ?? ''];
    return key === StockSort.Value && !this.$canViewValuation() ? undefined : key;
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageState.setPageSize(pageSize);
  }

  onSortChange(sort: TableSort): void {
    this.$sort.set(sort);
  }

  onClearFilters(): void {
    this.$searchInput.set('');
    this.categoryControl.setValue(null);
    this.lowOnlyControl.setValue(false);
  }

  onRetry(): void {
    this.store.list.reload();
  }

  onExport(): void {
    const { warehouseId, categoryId, search, lowOnly } = this.$query();
    this.reports.exportStockSummary
      .run({ warehouseId, categoryId, search, lowOnly })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }
}
