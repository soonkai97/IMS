import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DocumentListItem, DocumentStatus, DocumentType } from '@ims/api';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  EnumLabelPipe,
  DOCUMENT_STATUS_LABELS,
  I18nService,
  MessageKey,
  TranslatePipe
} from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import {
  DOCUMENT_ROUTE,
  DocumentQuery,
  DocumentsStore,
  LIST_SCOPE,
  ListScopePreference,
  STATUS_TONE
} from '@ims/features/documents';
import { WarehousesStore } from '@ims/features/warehousing';
import { DEFAULT_PAGE_SIZE, PERMISSION } from '@ims/models';
import {
  SelectOption,
  StatusTone,
  TabItem,
  TableColumn,
  columnWhen,
  UiButtonComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiSelectComponent,
  UiStatusChipComponent,
  UiTableComponent,
  UiTabsComponent
} from '@ims/ui';
import { controlSignal, createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

/** Receipts or issues (route data `type`): filters, status chips, cards on phones, server paging. */
@Component({
  selector: 'app-document-list-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
    EnumLabelPipe,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiSelectComponent,
    UiStatusChipComponent,
    UiTableComponent,
    UiTabsComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-list.component.html',
  styleUrl: './document-list.component.scss'
})
export class DocumentListPageComponent implements OnInit {
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly scopePreference = inject(ListScopePreference);
  private readonly store = inject(DocumentsStore);
  private readonly warehouses = inject(WarehousesStore);

  /** Route data. */
  $type = input.required<DocumentType>({ alias: 'type' });

  readonly statusControl = new FormControl<DocumentStatus | null>(null);
  readonly warehouseControl = new FormControl<number | null>(null);
  readonly fromControl = new FormControl('', { nonNullable: true });
  readonly toControl = new FormControl('', { nonNullable: true });

  $searchInput = signal('');
  /** On a phone the four filters hide behind one button so the first documents are on screen (NV-08). */
  $filtersOpen = signal(false);
  /** Mine = documents I created (`mine=true`); the choice is remembered per user. */
  $scope = signal<string>(this.scopePreference.initial());

  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $status = controlSignal(this.statusControl);
  private readonly $warehouseId = controlSignal(this.warehouseControl);
  private readonly $from = controlSignal(this.fromControl);
  private readonly $to = controlSignal(this.toControl);
  private readonly $filterKey = computed(() =>
    JSON.stringify([
      this.$type(),
      this.$scope(),
      this.$search(),
      this.$status(),
      this.$warehouseId(),
      this.$from(),
      this.$to()
    ])
  );
  private readonly pageState = createPageState(this.$filterKey);

  readonly $query = computed<DocumentQuery>(() => ({
    type: this.$type(),
    mine: this.$scope() === LIST_SCOPE.Mine ? true : undefined,
    status: this.$status() ?? undefined,
    warehouseId: this.$warehouseId() ?? undefined,
    from: this.$from() || undefined,
    to: this.$to() || undefined,
    search: this.$search() || undefined,
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $isIssue = computed(() => this.$type() === DocumentType.Issue);
  $canCreate = computed(() => this.permissions.has(PERMISSION.DocumentCreate));
  $hasFilters = computed(
    () =>
      this.$search() !== '' ||
      this.$status() !== null ||
      this.$warehouseId() !== null ||
      this.$from() !== '' ||
      this.$to() !== ''
  );
  /** Filters that are set inside the collapsed panel (the search box is always visible). */
  $panelFilterCount = computed(
    () =>
      [this.$status(), this.$warehouseId(), this.$from(), this.$to()].filter(
        value => value !== null && value !== ''
      ).length
  );
  $scopeTabs = computed<readonly TabItem[]>(() => [
    { id: LIST_SCOPE.Mine, label: this.i18n.t('docs.scope.mine') },
    { id: LIST_SCOPE.All, label: this.i18n.t('docs.scope.all') }
  ]);
  $statusOptions = computed<readonly SelectOption[]>(() =>
    Object.values(DocumentStatus).map(status => ({
      value: status,
      label: this.i18n.enumLabel(DOCUMENT_STATUS_LABELS, status)
    }))
  );
  $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );
  /** The amount column exists only for a caller with cost.view; the server sends null to the others (mvp-ux 1.4). */
  private readonly $canViewCost = computed(() => this.permissions.has(PERMISSION.CostView));
  $columns = computed<readonly TableColumn<DocumentListItem>[]>(() => [
    { key: 'no', header: this.i18n.t('doc.no'), value: row => row.no, primary: true },
    { key: 'date', header: this.i18n.t('doc.date'), phoneInline: true },
    {
      key: 'warehouse',
      header: this.i18n.t('doc.warehouse'),
      value: row => row.warehouseName,
      phoneInline: true
    },
    {
      key: 'partner',
      header: this.i18n.t(this.$isIssue() ? 'doc.customer' : 'doc.supplier'),
      value: row => row.partnerName ?? '',
      phoneInline: true
    },
    { key: 'qty', header: this.i18n.t('doc.totalQty'), align: 'end' },
    ...columnWhen<DocumentListItem>(this.$canViewCost(), {
      key: 'amount',
      header: this.i18n.t('doc.amount'),
      align: 'end',
      hideOnPhone: true
    }),
    { key: 'status', header: this.i18n.t('product.status'), phoneEnd: true }
  ]);
  $heading = computed(() => this.i18n.t(this.$isIssue() ? 'issues.title' : 'receipts.title'));
  $emptyKey = computed<MessageKey>(() => {
    if (this.$hasFilters()) {
      return 'docs.noMatch';
    }
    return this.$isIssue() ? 'issues.empty' : 'receipts.empty';
  });
  $newLink = computed(() => ['/', DOCUMENT_ROUTE[this.$type()], 'new']);

  readonly rowLink = (row: DocumentListItem): readonly unknown[] => [
    '/',
    DOCUMENT_ROUTE[row.type],
    row.id
  ];
  readonly tone: Readonly<Record<string, StatusTone>> = STATUS_TONE;
  readonly statusLabels = DOCUMENT_STATUS_LABELS;

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  ngOnInit(): void {
    this.warehouses.ensureLoaded();
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onScopeChange(scope: string): void {
    this.$scope.set(scope);
    this.scopePreference.remember(scope === LIST_SCOPE.Mine ? LIST_SCOPE.Mine : LIST_SCOPE.All);
  }

  onToggleFilters(): void {
    this.$filtersOpen.update(open => !open);
  }

  onClearFilters(): void {
    this.$searchInput.set('');
    this.statusControl.setValue(null);
    this.warehouseControl.setValue(null);
    this.fromControl.setValue('');
    this.toControl.setValue('');
  }

  onRetry(): void {
    this.store.list.reload();
  }
}
