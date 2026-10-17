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
  AppSignedQtyPipe,
  AppUnitPricePipe,
  DOCUMENT_STATUS_LABELS,
  DOCUMENT_TYPE_LABELS,
  I18nService,
  TranslatePipe
} from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { MovementFilter, MovementQuery, ReportsStore } from '@ims/features/reports';
import { WarehousesStore } from '@ims/features/warehousing';
import { DEFAULT_PAGE_SIZE, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  TabItem,
  TableColumn,
  columnWhen,
  UiButtonComponent,
  UiCardComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiDateFieldComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSelectComponent,
  UiStatusChipComponent,
  UiTableComponent,
  UiTabPanelDirective,
  UiTabsComponent
} from '@ims/ui';
import { controlSignal, createPageState, isDateRangeInvalid, loadOnChange } from '@ims/utils';

const TAB_MOVEMENTS = 'movements';
const TAB_EXPORTS = 'exports';

/** Receipt / issue detail on screen and the Excel exports (RPT-01). Exports can take a while: buttons show progress. */
@Component({
  selector: 'app-reports-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
    AppSignedQtyPipe,
    AppUnitPricePipe,
    ReactiveFormsModule,
    TranslatePipe,
    UiButtonComponent,
    UiCardComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiDateFieldComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSelectComponent,
    UiStatusChipComponent,
    UiTableComponent,
    UiTabPanelDirective,
    UiTabsComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss'
})
export class ReportsPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(ReportsStore);
  private readonly warehouses = inject(WarehousesStore);

  readonly typeControl = new FormControl<DocumentType | null>(null);
  readonly warehouseControl = new FormControl<PublicId | null>(null);
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

  $rangeInvalid = computed(() => isDateRangeInvalid(this.$from(), this.$to()));
  // The sentence under To: ours before a request is sent, the server's (errors.to) if it still answers 400.
  private readonly $serverToError = computed(
    () => this.store.movements.$operation().error?.fieldErrors['to']?.[0] ?? null
  );
  $toError = computed(() =>
    this.$rangeInvalid() ? this.i18n.t('filter.rangeInvalid') : this.$serverToError()
  );
  // The sentence under To explains the empty screen, so no table (and no "No data for these filters.") shows with it.
  $blocked = computed(() => this.$rangeInvalid() || this.$serverToError() !== null);
  readonly rangeHintId = 'reports-range-hint';
  // A bad range or a failed call shows no old rows and no old "1-6 of 6" footer.
  $page = computed(() =>
    this.$rangeInvalid() || this.store.movements.$isFailed()
      ? undefined
      : this.store.movements.$data()
  );
  $isLoading = computed(() => !this.$rangeInvalid() && this.store.movements.$isLoading());
  // A field error is shown at the field, not as "Something went wrong" with a Retry that cannot work.
  $failed = computed(
    () =>
      !this.$rangeInvalid() && this.store.movements.$isFailed() && this.$serverToError() === null
  );
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
  private readonly $canViewCost = computed(() => this.permissions.has(PERMISSION.CostView));
  $columns = computed<readonly TableColumn<MovementRow>[]>(() => [
    // The reversal badge sits next to the number so it also shows on the phone card, where Status is hidden.
    { key: 'docNo', type: 'code', header: this.i18n.t('doc.no'), primary: true },
    { key: 'date', type: 'date', header: this.i18n.t('reports.postedAt') },
    {
      key: 'type',
      header: this.i18n.t('doc.type'),
      value: row => this.i18n.enumLabel(DOCUMENT_TYPE_LABELS, row.type),
      hideOnPhone: true
    },
    {
      key: 'status',
      type: 'status',
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
    { key: 'sku', type: 'code', header: this.i18n.t('product.sku'), value: row => row.sku },
    { key: 'product', header: this.i18n.t('reports.product'), value: row => row.productName },
    { key: 'qty', header: this.i18n.t('doc.qty'), align: 'end' },
    { key: 'unit', header: this.i18n.t('doc.unit'), value: row => row.uomCode },
    { key: 'effect', header: this.i18n.t('reports.stockEffect'), align: 'end' },
    // Cost columns need cost.view; the server sends null to the others (docs/design/mvp-ux.md 1.4).
    ...columnWhen<MovementRow>(this.$canViewCost(), {
      key: 'unitCost',
      header: this.i18n.t('reports.unitCost'),
      align: 'end',
      hideOnPhone: true
    }),
    ...columnWhen<MovementRow>(this.$canViewCost(), {
      key: 'amount',
      header: this.i18n.t('doc.amount'),
      align: 'end',
      hideOnPhone: true
    })
  ]);

  constructor() {
    // A bad range sends no request (the API would answer 400 anyway).
    loadOnChange(this.$query, query => {
      if (!this.$rangeInvalid()) {
        this.store.movements.load(query);
      }
    });
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
