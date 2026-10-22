import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked
} from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PartnerType, ReorderSuggestion } from '@ims/api';
import { AppMoneyPipe, AppQtyPipe, I18nService, MessageKey, TranslatePipe } from '@ims/core/i18n';
import { AuthService, PermissionService } from '@ims/core/identity';
import {
  MAX_PREFILL_LINES,
  PURCHASE_ORDER_ROUTE,
  ReorderQuery,
  ReorderStore,
  buildReorderEntryLink,
  isValidReorderQty,
  reorderValue
} from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { API_MIN_SEARCH_LENGTH, PAGE_SIZES, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  TableColumn,
  columnWhen,
  UiButtonComponent,
  UiCellDirective,
  UiCheckboxComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiSelectComponent,
  UiTableComponent
} from '@ims/ui';
import { controlSignal, createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

/** Edits and selection belong to one view of the list; `key` says which (a new key means they are gone). */
interface Keyed<Value> {
  readonly key: string;
  readonly value: Value;
}

/**
 * Reorder: what is below its safety stock in one warehouse, net of the open orders, with the quantity to order in an editable box.
 * Rows are selected on one page at a time (a new page, search or warehouse clears the selection; the screen says so), the supplier
 * is chosen once, and "Create purchase order" opens the order editor through its entry link. The editor checks everything again.
 */
@Component({
  selector: 'app-reorder-page',
  imports: [
    AppMoneyPipe,
    AppQtyPipe,
    FormsModule,
    ReactiveFormsModule,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiCheckboxComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiSelectComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reorder.component.html',
  styleUrl: './reorder.component.scss'
})
export class ReorderPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly partners = inject(PartnersStore);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly store = inject(ReorderStore);
  private readonly warehouses = inject(WarehousesStore);

  readonly backLink = ['/', PURCHASE_ORDER_ROUTE];
  readonly warehouseControl = new FormControl<PublicId | null>(null);
  readonly supplierControl = new FormControl<PublicId | null>(null);
  readonly pageSizes = PAGE_SIZES;
  readonly maxLines = MAX_PREFILL_LINES;

  readonly $searchInput = signal('');
  /** "Create purchase order" was pressed: the problems are shown from then on (the button itself is never greyed out). */
  readonly $tried = signal(false);

  private readonly $typed = debouncedSignal(this.$searchInput);
  /** What is sent: a search under the API minimum is no search. */
  private readonly $search = computed(() => {
    const text = this.$typed().trim();
    return text.length >= API_MIN_SEARCH_LENGTH ? text : '';
  });
  private readonly $warehouseId = controlSignal(this.warehouseControl);
  private readonly $supplierId = controlSignal(this.supplierControl);
  private readonly pageState = createPageState(
    computed(() => JSON.stringify([this.$warehouseId(), this.$search()]))
  );

  /** Null until a warehouse is chosen: the API refuses a request without one, so none is sent. */
  readonly $query = computed<ReorderQuery | null>(() => {
    const warehouseId = this.$warehouseId();
    return warehouseId === null
      ? null
      : {
          warehouseId,
          search: this.$search() || undefined,
          page: this.pageState.$page(),
          pageSize: this.pageState.$pageSize()
        };
  });

  /** By permission, never by role: the editor needs both rights, so the action is offered to the holders of both. */
  readonly $canCreate = computed(
    () => this.permissions.has(PERMISSION.PoManage) && this.permissions.has(PERMISSION.CostView)
  );
  readonly $canViewCost = computed(() => this.permissions.has(PERMISSION.CostView));

  readonly $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );
  readonly $supplierOptions = computed<readonly SelectOption[]>(() =>
    this.partners.$partners().map(partner => ({ value: partner.id, label: partner.name }))
  );

  /** Rows of the chosen warehouse only: the answer to the one before is still held while the next one loads. */
  readonly $rows = computed<readonly ReorderSuggestion[]>(() =>
    (this.store.suggestions.$data()?.items ?? []).filter(
      row => row.warehouseId === this.$warehouseId()
    )
  );
  readonly $page = computed(() =>
    this.$query() === null ? undefined : this.store.suggestions.$data()
  );
  readonly $isLoading = this.store.suggestions.$isLoading;
  readonly $failed = this.store.suggestions.$isFailed;
  readonly $emptyKey = computed<MessageKey>(() =>
    this.$search() === '' ? 'reorder.empty' : 'reorder.noMatch'
  );

  // The edited quantities belong to one warehouse; the selection to one page of one search.
  private readonly $edits = signal<Keyed<ReadonlyMap<PublicId, number | null>>>({
    key: '',
    value: new Map()
  });
  private readonly $picked = signal<Keyed<ReadonlySet<PublicId>>>({ key: '', value: new Set() });
  private readonly $editKey = computed(() => this.$warehouseId() ?? '');
  private readonly $pickKey = computed(() => JSON.stringify([this.$query()]));
  private readonly $editedQty = computed(() =>
    this.$edits().key === this.$editKey() ? this.$edits().value : new Map<PublicId, number | null>()
  );
  readonly $pickedIds = computed(() =>
    this.$picked().key === this.$pickKey() ? this.$picked().value : new Set<PublicId>()
  );
  readonly $selectedRows = computed(() =>
    this.$rows().filter(row => this.$pickedIds().has(row.productId))
  );
  readonly $allSelected = computed(
    () => this.$rows().length > 0 && this.$selectedRows().length === this.$rows().length
  );
  readonly $overflow = computed(() => Math.max(0, this.$selectedRows().length - MAX_PREFILL_LINES));
  private readonly $hasInvalidQty = computed(() =>
    this.$selectedRows().some(row => !isValidReorderQty(this.qtyOf(row)))
  );

  readonly $supplierError = computed(() =>
    this.$tried() && this.$supplierId() === null ? this.i18n.t('reorder.supplierRequired') : null
  );
  readonly $problem = computed<MessageKey | null>(() => {
    if (!this.$tried()) {
      return null;
    }
    if (this.$selectedRows().length === 0) {
      return 'reorder.nothingSelected';
    }
    return this.$hasInvalidQty() ? 'reorder.fixQuantities' : null;
  });

  readonly $columns = computed<readonly TableColumn<ReorderSuggestion>[]>(() => [
    ...columnWhen<ReorderSuggestion>(this.$canCreate(), {
      key: 'select',
      type: 'action',
      header: this.i18n.t('reorder.select'),
      phoneInline: true
    }),
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
      key: 'unit',
      header: this.i18n.t('doc.unit'),
      value: row => row.uomCode,
      phoneInline: true
    },
    { key: 'onHand', header: this.i18n.t('stock.onHand'), align: 'end' },
    { key: 'safety', header: this.i18n.t('product.safetyStock'), align: 'end' },
    { key: 'onOrder', header: this.i18n.t('reorder.onOrder'), align: 'end' },
    { key: 'qty', header: this.i18n.t('reorder.qty'), align: 'end' },
    ...columnWhen<ReorderSuggestion>(this.$canViewCost(), {
      key: 'value',
      header: this.i18n.t('reorder.value'),
      align: 'end'
    })
  ]);

  constructor() {
    // The stores outlive a sign-out: the next user must not see the last user's rows, suppliers or warehouses for a moment.
    this.store.suggestions.reset();
    this.partners.list.reset();
    this.warehouses.list.reset();
    loadOnChange(this.$query, query => {
      if (query) {
        this.store.suggestions.load(query);
      } else {
        this.store.suggestions.reset();
      }
    });
  }

  /** The remembered warehouse (if it is still the user's), else the only one the user has. */
  private readonly chooseWarehouse = effect(() => {
    const options = this.$warehouseOptions();
    untracked(() => {
      if (this.warehouseControl.value !== null) {
        return;
      }
      const remembered = this.store.lastWarehouse(this.auth.$user()?.id);
      const known = options.find(option => option.value === remembered);
      const only = options.length === 1 ? options[0] : undefined;
      const choice = known ?? only;
      if (choice) {
        this.warehouseControl.setValue(choice.value as PublicId);
      }
    });
  });

  private readonly rememberWarehouse = effect(() => {
    const warehouseId = this.$warehouseId();
    untracked(() => this.store.remember(this.auth.$user()?.id, warehouseId));
  });

  ngOnInit(): void {
    this.warehouses.list.load(false);
    this.partners.loadActive(PartnerType.Supplier);
  }

  qtyOf(row: ReorderSuggestion): number | null {
    const edits = this.$editedQty();
    return edits.has(row.productId) ? (edits.get(row.productId) ?? null) : row.suggestedQty;
  }

  qtyError(row: ReorderSuggestion): string | null {
    return isValidReorderQty(this.qtyOf(row)) ? null : this.i18n.t('reorder.qtyInvalid');
  }

  /** The server's value for the suggestion, scaled to the quantity in the box; null shows as a dash, never 0. */
  valueOf(row: ReorderSuggestion): number | null {
    return reorderValue(row, this.qtyOf(row));
  }

  isPicked(row: ReorderSuggestion): boolean {
    return this.$pickedIds().has(row.productId);
  }

  onQty(row: ReorderSuggestion, qty: number | null): void {
    const next = new Map(this.$editedQty());
    next.set(row.productId, qty ?? null);
    this.$edits.set({ key: this.$editKey(), value: next });
  }

  onPick(row: ReorderSuggestion, picked: boolean): void {
    const next = new Set(this.$pickedIds());
    if (picked) {
      next.add(row.productId);
    } else {
      next.delete(row.productId);
    }
    this.$picked.set({ key: this.$pickKey(), value: next });
  }

  onPickAll(picked: boolean): void {
    const ids = picked ? new Set(this.$rows().map(row => row.productId)) : new Set<PublicId>();
    this.$picked.set({ key: this.$pickKey(), value: ids });
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageState.setPageSize(pageSize);
  }

  onRetry(): void {
    this.store.suggestions.reload();
  }

  onCreate(): void {
    this.$tried.set(true);
    const warehouseId = this.$warehouseId();
    const supplierId = this.$supplierId();
    const rows = this.$selectedRows();
    if (warehouseId === null || supplierId === null || rows.length === 0 || this.$hasInvalidQty()) {
      // Take the person to the first box that needs a correction.
      queueMicrotask(() =>
        this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
      );
      return;
    }
    const link = buildReorderEntryLink(
      warehouseId,
      supplierId,
      rows.map(row => ({ sku: row.sku, qty: this.qtyOf(row) ?? 0 }))
    );
    void this.router.navigate([...link.commands], { queryParams: link.queryParams });
  }
}
