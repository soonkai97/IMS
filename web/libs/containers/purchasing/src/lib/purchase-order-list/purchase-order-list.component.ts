import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PartnerType, PurchaseOrderListItem, PurchaseOrderStatus } from '@ims/api';
import {
  AppDatePipe,
  AppMoneyPipe,
  EnumLabelPipe,
  I18nService,
  MessageKey,
  PURCHASE_ORDER_STATUS_LABELS,
  TranslatePipe
} from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import {
  PURCHASE_ORDER_ROUTE,
  PO_STATUS_TONE,
  PurchaseOrderQuery,
  PurchaseOrdersStore
} from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { API_MIN_SEARCH_LENGTH, PAGE_SIZES, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  StatusTone,
  TableColumn,
  columnWhen,
  UiButtonComponent,
  UiCellDirective,
  UiDateFieldComponent,
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
import {
  controlSignal,
  createPageState,
  debouncedSignal,
  isDateRangeInvalid,
  loadOnChange
} from '@ims/utils';

/**
 * Purchase orders: filters, status chips, cards on phones, server paging. An approver has no inbox row for orders, so the
 * Status filter offers "Submitted" and a row the server says the caller can act on (`canAct`) is marked "Waiting for you".
 */
@Component({
  selector: 'app-purchase-order-list-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    EnumLabelPipe,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiDateFieldComponent,
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
  templateUrl: './purchase-order-list.component.html',
  styleUrl: './purchase-order-list.component.scss'
})
export class PurchaseOrderListPageComponent implements OnInit {
  private readonly i18n = inject(I18nService);
  private readonly partners = inject(PartnersStore);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(PurchaseOrdersStore);
  private readonly warehouses = inject(WarehousesStore);

  readonly statusControl = new FormControl<PurchaseOrderStatus | null>(null);
  readonly supplierControl = new FormControl<PublicId | null>(null);
  readonly warehouseControl = new FormControl<PublicId | null>(null);
  readonly fromControl = new FormControl('', { nonNullable: true });
  readonly toControl = new FormControl('', { nonNullable: true });

  /** By permission, never by role: whoever may manage orders may start one. */
  readonly $canCreate = computed(
    () => this.permissions.has(PERMISSION.PoManage) && this.permissions.has(PERMISSION.CostView)
  );

  $searchInput = signal('');
  /** On a phone the filters hide behind one button so the first orders are on screen. */
  $filtersOpen = signal(false);

  private readonly $typed = debouncedSignal(this.$searchInput);
  /** What is sent: a search under the API minimum is no search, so 1 or 2 letters leave the list as it is. */
  private readonly $search = computed(() => {
    const text = this.$typed().trim();
    return text.length >= API_MIN_SEARCH_LENGTH ? text : '';
  });
  private readonly $status = controlSignal(this.statusControl);
  private readonly $supplierId = controlSignal(this.supplierControl);
  private readonly $warehouseId = controlSignal(this.warehouseControl);
  private readonly $from = controlSignal(this.fromControl);
  private readonly $to = controlSignal(this.toControl);
  private readonly $filterKey = computed(() =>
    JSON.stringify([
      this.$search(),
      this.$status(),
      this.$supplierId(),
      this.$warehouseId(),
      this.$from(),
      this.$to()
    ])
  );
  readonly pageSizes = PAGE_SIZES;
  private readonly pageState = createPageState(this.$filterKey);

  readonly $query = computed<PurchaseOrderQuery>(() => ({
    status: this.$status() ?? undefined,
    supplierId: this.$supplierId() ?? undefined,
    warehouseId: this.$warehouseId() ?? undefined,
    from: this.$from() || undefined,
    to: this.$to() || undefined,
    search: this.$search() || undefined,
    page: this.pageState.$page(),
    pageSize: this.pageState.$pageSize()
  }));

  // No request is sent for a reversed range (the API would answer 400), and no old rows stay.
  $rangeInvalid = computed(() => isDateRangeInvalid(this.$from(), this.$to()));
  $toError = computed(() => (this.$rangeInvalid() ? this.i18n.t('filter.rangeInvalid') : null));
  $page = computed(() => (this.$rangeInvalid() ? undefined : this.store.list.$data()));
  $isLoading = computed(() => !this.$rangeInvalid() && this.store.list.$isLoading());
  $failed = computed(() => !this.$rangeInvalid() && this.store.list.$isFailed());
  $hasFilters = computed(
    () =>
      this.$search() !== '' ||
      this.$status() !== null ||
      this.$supplierId() !== null ||
      this.$warehouseId() !== null ||
      this.$from() !== '' ||
      this.$to() !== ''
  );
  /** Filters set inside the collapsed panel (the search box is always visible). */
  $panelFilterCount = computed(
    () =>
      [this.$status(), this.$supplierId(), this.$warehouseId(), this.$from(), this.$to()].filter(
        value => value !== null && value !== ''
      ).length
  );
  $statusOptions = computed<readonly SelectOption[]>(() =>
    Object.values(PurchaseOrderStatus).map(status => ({
      value: status,
      label: this.i18n.enumLabel(PURCHASE_ORDER_STATUS_LABELS, status)
    }))
  );
  $supplierOptions = computed<readonly SelectOption[]>(() =>
    this.partners.$partners().map(partner => ({ value: partner.id, label: partner.name }))
  );
  $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );
  /** The amount column exists only for a caller with cost.view; the server sends null to the others. */
  private readonly $canViewCost = computed(() => this.permissions.has(PERMISSION.CostView));
  $columns = computed<readonly TableColumn<PurchaseOrderListItem>[]>(() => [
    { key: 'no', type: 'code', header: this.i18n.t('doc.no'), value: row => row.no, primary: true },
    { key: 'date', type: 'date', header: this.i18n.t('doc.date'), phoneInline: true },
    {
      key: 'supplier',
      header: this.i18n.t('doc.supplier'),
      value: row => row.supplierName,
      phoneInline: true
    },
    {
      key: 'warehouse',
      header: this.i18n.t('doc.warehouse'),
      value: row => row.warehouseName,
      phoneInline: true
    },
    ...columnWhen<PurchaseOrderListItem>(this.$canViewCost(), {
      key: 'amount',
      header: this.i18n.t('doc.amount'),
      align: 'end'
    }),
    { key: 'status', type: 'status', header: this.i18n.t('product.status'), phoneEnd: true }
  ]);
  $emptyKey = computed<MessageKey>(() => {
    // No search ran for a reversed range, so "no orders match" would be untrue.
    if (this.$rangeInvalid()) {
      return 'filter.rangeInvalid';
    }
    return this.$hasFilters() ? 'po.noMatch' : 'po.empty';
  });

  readonly rowLink = (row: PurchaseOrderListItem): readonly unknown[] => [
    '/',
    PURCHASE_ORDER_ROUTE,
    row.id
  ];
  readonly tone: Readonly<Record<string, StatusTone>> = PO_STATUS_TONE;
  readonly statusLabels = PURCHASE_ORDER_STATUS_LABELS;

  constructor() {
    // The store outlives a sign-out: the next user must not see the last user's orders for a moment.
    this.store.list.reset();
    loadOnChange(this.$query, query => {
      if (!this.$rangeInvalid()) {
        this.store.list.load(query);
      }
    });
  }

  ngOnInit(): void {
    this.warehouses.ensureLoaded();
    this.partners.loadActive(PartnerType.Supplier);
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageState.setPageSize(pageSize);
  }

  onToggleFilters(): void {
    this.$filtersOpen.update(open => !open);
  }

  onClearFilters(): void {
    this.$searchInput.set('');
    this.statusControl.setValue(null);
    this.supplierControl.setValue(null);
    this.warehouseControl.setValue(null);
    this.fromControl.setValue('');
    this.toControl.setValue('');
  }

  onRetry(): void {
    this.store.list.reload();
  }
}
