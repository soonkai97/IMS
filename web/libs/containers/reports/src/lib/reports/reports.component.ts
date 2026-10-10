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
import { DocumentType, MovementRow } from '@ims/api';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_STATUS_LABELS,
  DOCUMENT_TYPE_LABELS,
  I18nService,
  TranslatePipe
} from '@ims/core/i18n';
import { MovementFilter, MovementQuery, ReportsStore } from '@ims/features/reports';
import { WarehousesStore } from '@ims/features/warehousing';
import { DEFAULT_PAGE_SIZE } from '@ims/models';
import {
  SelectOption,
  TabItem,
  TableColumn,
  UiButtonComponent,
  UiCardComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSelectComponent,
  UiTableComponent,
  UiTabsComponent
} from '@ims/ui';
import { controlSignal, createPageState, loadOnChange } from '@ims/utils';

const TAB_MOVEMENTS = 'movements';
const TAB_EXPORTS = 'exports';

/** Receipt / issue detail on screen and the Excel exports (RPT-01). Exports can take a while: buttons show progress. */
@Component({
  selector: 'app-reports-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
    ReactiveFormsModule,
    TranslatePipe,
    UiButtonComponent,
    UiCardComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSelectComponent,
    UiTableComponent,
    UiTabsComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss'
})
export class ReportsPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly store = inject(ReportsStore);
  private readonly warehouses = inject(WarehousesStore);

  readonly typeControl = new FormControl<DocumentType | null>(null);
  readonly warehouseControl = new FormControl<number | null>(null);
  readonly fromControl = new FormControl('', { nonNullable: true });
  readonly toControl = new FormControl('', { nonNullable: true });

  $tab = signal<string>(TAB_MOVEMENTS);

  private readonly $type = controlSignal(this.typeControl);
  private readonly $warehouseId = controlSignal(this.warehouseControl);
  private readonly $from = controlSignal(this.fromControl);
  private readonly $to = controlSignal(this.toControl);
  private readonly $filterKey = computed(() =>
    JSON.stringify([this.$type(), this.$warehouseId(), this.$from(), this.$to()])
  );
  private readonly pageState = createPageState(this.$filterKey);

  private readonly $filter = computed<MovementFilter>(() => ({
    type: this.$type() ?? undefined,
    warehouseId: this.$warehouseId() ?? undefined,
    from: this.$from() || undefined,
    to: this.$to() || undefined
  }));
  readonly $query = computed<MovementQuery>(() => ({
    ...this.$filter(),
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $page = this.store.movements.$data;
  $isLoading = this.store.movements.$isLoading;
  $failed = this.store.movements.$isFailed;
  $isMovementsTab = computed(() => this.$tab() === TAB_MOVEMENTS);
  $exportingMovements = this.store.exportMovements.$isRunning;
  $exportingStock = this.store.exportStockSummary.$isRunning;
  $exportingLedger = this.store.exportLedger.$isRunning;
  $exportingLow = this.store.exportLowStock.$isRunning;
  $tabs = computed<readonly TabItem[]>(() => [
    { id: TAB_MOVEMENTS, label: this.i18n.t('reports.tab.movements') },
    { id: TAB_EXPORTS, label: this.i18n.t('reports.tab.exports') }
  ]);
  $typeOptions = computed<readonly SelectOption[]>(() => [
    { value: DocumentType.Receipt, label: this.i18n.t('docType.Receipt') },
    { value: DocumentType.Issue, label: this.i18n.t('docType.Issue') }
  ]);
  $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );
  $columns = computed<readonly TableColumn<MovementRow>[]>(() => [
    { key: 'docNo', header: this.i18n.t('doc.no'), value: row => row.docNo, primary: true },
    { key: 'date', header: this.i18n.t('reports.postedAt') },
    {
      key: 'type',
      header: this.i18n.t('doc.type'),
      value: row => this.i18n.enumLabel(DOCUMENT_TYPE_LABELS, row.type),
      hideOnPhone: true
    },
    {
      key: 'status',
      header: this.i18n.t('product.status'),
      value: row => this.i18n.enumLabel(DOCUMENT_STATUS_LABELS, row.status),
      hideOnPhone: true
    },
    {
      key: 'warehouse',
      header: this.i18n.t('stock.warehouse'),
      value: row => row.warehouseCode,
      hideOnPhone: true
    },
    {
      key: 'partner',
      header: this.i18n.t('doc.partner'),
      value: row => row.partner ?? '',
      hideOnPhone: true
    },
    { key: 'sku', header: this.i18n.t('product.sku'), value: row => row.sku },
    { key: 'qty', header: this.i18n.t('doc.qty'), align: 'end' },
    { key: 'amount', header: this.i18n.t('doc.amount'), align: 'end', hideOnPhone: true }
  ]);

  constructor() {
    loadOnChange(this.$query, query => this.store.movements.load(query));
  }

  ngOnInit(): void {
    this.warehouses.ensureLoaded();
  }

  onTabChange(tab: string): void {
    this.$tab.set(tab);
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onRetry(): void {
    this.store.movements.reload();
  }

  onExportMovements(): void {
    this.store.exportMovements
      .run(this.$filter())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  onExportStock(): void {
    const { warehouseId } = this.$filter();
    this.store.exportStockSummary
      .run({ warehouseId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  onExportLedger(): void {
    const { warehouseId, from, to } = this.$filter();
    this.store.exportLedger
      .run({ warehouseId, from, to })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  onExportLowStock(): void {
    this.store.exportLowStock.run().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}
