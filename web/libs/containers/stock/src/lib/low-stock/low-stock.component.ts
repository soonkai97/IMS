import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LowStockAlert } from '@ims/api';
import { AppQtyPipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { ReportsStore } from '@ims/features/reports';
import { LowStockQuery, StockStore } from '@ims/features/stock';
import { DEFAULT_PAGE_SIZE, PERMISSION } from '@ims/models';
import {
  TableColumn,
  UiButtonComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { createPageState, loadOnChange } from '@ims/utils';

/** Products at or below safety stock, worst first (T16). */
@Component({
  selector: 'app-low-stock-page',
  imports: [
    AppQtyPipe,
    RouterLink,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './low-stock.component.html',
  styleUrl: './low-stock.component.scss'
})
export class LowStockPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly reports = inject(ReportsStore);
  private readonly store = inject(StockStore);

  private readonly pageState = createPageState(computed(() => 'low-stock'));

  readonly $query = computed<LowStockQuery>(() => ({
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $page = this.store.lowStock.$data;
  $isLoading = this.store.lowStock.$isLoading;
  $failed = this.store.lowStock.$isFailed;
  $isExporting = this.reports.exportLowStock.$isRunning;
  $canExport = computed(() => this.permissions.has(PERMISSION.ReportsRead));
  $canCreate = computed(() => this.permissions.has(PERMISSION.DocumentCreate));
  $columns = computed<readonly TableColumn<LowStockAlert>[]>(() => [
    {
      key: 'sku',
      type: 'code',
      header: this.i18n.t('product.sku'),
      value: row => row.sku,
      primary: true
    },
    {
      key: 'name',
      header: this.i18n.t('product.name'),
      value: row => row.productName,
      phoneInline: true
    },
    {
      key: 'warehouse',
      header: this.i18n.t('stock.warehouse'),
      value: row => row.warehouseName,
      phoneInline: true
    },
    { key: 'onHand', header: this.i18n.t('stock.onHand'), align: 'end' },
    { key: 'safety', header: this.i18n.t('product.safetyStock'), align: 'end', hideOnPhone: true },
    { key: 'short', header: this.i18n.t('lowStock.shortBy'), align: 'end', phoneEnd: true },
    {
      key: 'actions',
      type: 'action',
      header: this.i18n.t('common.actions'),
      align: 'end',
      phoneInline: true
    }
  ]);

  readonly rowLink = (row: LowStockAlert): readonly unknown[] => ['/stock', row.productId];
  /** "Create receipt" opens the form with this product and warehouse already chosen (design 3.8, NV-13). */
  readonly receiptParams = (row: LowStockAlert): Record<string, string | number> => ({
    sku: row.sku,
    warehouseId: row.warehouseId
  });

  constructor() {
    loadOnChange(this.$query, query => this.store.lowStock.load(query));
  }

  ngOnInit(): void {
    this.store.summary.load();
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onRetry(): void {
    this.store.lowStock.reload();
  }

  onExport(): void {
    this.reports.exportLowStock.run().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}
