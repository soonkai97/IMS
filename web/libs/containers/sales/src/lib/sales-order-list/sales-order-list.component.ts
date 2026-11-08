import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { PartnerType, SalesOrderListItem, SalesOrderStatus } from '@ims/api';
import {
  AppDatePipe,
  AppMoneyPipe,
  EnumLabelPipe,
  I18nService,
  MessageKey,
  SALES_ORDER_STATUS_LABELS,
  TranslatePipe
} from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import {
  SALES_ORDER_ROUTE,
  SO_STATUS_TONE,
  SalesOrderQuery,
  SalesOrdersStore
} from '@ims/features/sales';
import { PartnersStore } from '@ims/features/warehousing';
import { API_MIN_SEARCH_LENGTH, PAGE_SIZES, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  StatusTone,
  TableColumn,
  columnWhen,
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

/**
 * Sales orders: filters, status chips, cards on phones, server paging. "Waiting for my approval" asks for the Submitted orders and
 * keeps the rows the server says the caller can act on (`canAct`; the API has no such filter). The amount column exists only for a
 * holder of so.manage, the people the API sends amounts to: a selling price is not cost, so cost.view has nothing to do with it.
 */
@Component({
  selector: 'app-sales-order-list-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    EnumLabelPipe,
    ReactiveFormsModule,
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
  templateUrl: './sales-order-list.component.html',
  styleUrl: './sales-order-list.component.scss'
})
export class SalesOrderListPageComponent implements OnInit {
  private readonly i18n = inject(I18nService);
  private readonly partners = inject(PartnersStore);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(SalesOrdersStore);

  readonly statusControl = new FormControl<SalesOrderStatus | null>(null);
  readonly customerControl = new FormControl<PublicId | null>(null);
  readonly awaitingControl = new FormControl(false, { nonNullable: true });

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
  private readonly $customerId = controlSignal(this.customerControl);
  private readonly $awaiting = controlSignal(this.awaitingControl);
  private readonly $filterKey = computed(() =>
    JSON.stringify([this.$search(), this.$status(), this.$customerId(), this.$awaiting()])
  );
  readonly pageSizes = PAGE_SIZES;
  private readonly pageState = createPageState(this.$filterKey);

  readonly $query = computed<SalesOrderQuery>(() => ({
    status: this.$awaiting() ? SalesOrderStatus.Submitted : (this.$status() ?? undefined),
    customerId: this.$customerId() ?? undefined,
    search: this.$search() || undefined,
    page: this.pageState.$page(),
    pageSize: this.pageState.$pageSize()
  }));

  readonly $rows = computed<readonly SalesOrderListItem[]>(() => {
    const items = this.store.list.$data()?.items ?? [];
    return this.$awaiting() ? items.filter(item => item.canAct) : items;
  });
  readonly $page = this.store.list.$data;
  readonly $isLoading = this.store.list.$isLoading;
  readonly $failed = this.store.list.$isFailed;
  readonly $hasFilters = computed(
    () =>
      this.$search() !== '' ||
      this.$status() !== null ||
      this.$customerId() !== null ||
      this.$awaiting()
  );
  /** Filters set inside the collapsed panel (the search box is always visible). */
  readonly $panelFilterCount = computed(
    () =>
      [this.$status() !== null, this.$customerId() !== null, this.$awaiting()].filter(Boolean)
        .length
  );
  readonly $statusOptions = computed<readonly SelectOption[]>(() =>
    Object.values(SalesOrderStatus).map(status => ({
      value: status,
      label: this.i18n.enumLabel(SALES_ORDER_STATUS_LABELS, status)
    }))
  );
  readonly $customerOptions = computed<readonly SelectOption[]>(() =>
    this.partners.$partners().map(partner => ({ value: partner.id, label: partner.name }))
  );
  private readonly $canViewMoney = computed(() => this.permissions.has(PERMISSION.SoManage));
  readonly $columns = computed<readonly TableColumn<SalesOrderListItem>[]>(() => [
    { key: 'no', type: 'code', header: this.i18n.t('doc.no'), value: row => row.no, primary: true },
    { key: 'date', type: 'date', header: this.i18n.t('doc.date'), phoneInline: true },
    {
      key: 'customer',
      header: this.i18n.t('doc.customer'),
      value: row => row.customerName,
      phoneInline: true
    },
    {
      key: 'warehouse',
      header: this.i18n.t('doc.warehouse'),
      value: row => row.warehouseName,
      phoneInline: true
    },
    ...columnWhen<SalesOrderListItem>(this.$canViewMoney(), {
      key: 'amount',
      header: this.i18n.t('doc.amount'),
      align: 'end'
    }),
    { key: 'status', type: 'status', header: this.i18n.t('product.status'), phoneEnd: true }
  ]);
  readonly $emptyKey = computed<MessageKey>(() => (this.$hasFilters() ? 'so.noMatch' : 'so.empty'));

  readonly rowLink = (row: SalesOrderListItem): readonly unknown[] => [
    '/',
    SALES_ORDER_ROUTE,
    row.id
  ];
  readonly tone: Readonly<Record<string, StatusTone>> = SO_STATUS_TONE;
  readonly statusLabels = SALES_ORDER_STATUS_LABELS;

  constructor() {
    // The store outlives a sign-out: the next user must not see the last user's orders for a moment.
    this.store.list.reset();
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  ngOnInit(): void {
    this.partners.loadActive(PartnerType.Customer);
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
    this.customerControl.setValue(null);
    this.awaitingControl.setValue(false);
  }

  onRetry(): void {
    this.store.list.reload();
  }
}
