import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { EMPTY, catchError, concatMap, filter, from, map, startWith, tap } from 'rxjs';
import { DocumentType, PartnerType } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { handledInline } from '@ims/core/http';
import { AppMoneyPipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { HasUnsavedChanges, PageTitleService } from '@ims/core/navigation';
import { ToastService } from '@ims/core/notification';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import {
  LineGroup,
  LineValue,
  LinesArray,
  computeTotals,
  createLineGroup,
  isValidQuantity,
  isValidUnitCost,
  lineFromProduct
} from '@ims/features/documents';
import {
  PO_ACTION,
  PURCHASE_ORDER_ROUTE,
  PoHeaderGroup,
  PurchaseOrdersStore,
  createPoHeader,
  headerFromOrder,
  isStaleOrderError,
  lineFromOrderLine,
  parsePrefillLines,
  toPoActions,
  toPurchaseOrderRef,
  toSaveRequest
} from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { API_ERROR_KIND, MAX_DOCUMENT_LINES, MAX_REMARK_LENGTH } from '@ims/models';
import {
  SelectOption,
  UiBannerComponent,
  UiButtonComponent,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPublishHeightDirective,
  UiSelectComponent,
  UiSkeletonComponent,
  UiTextareaComponent
} from '@ims/ui';
import {
  applyFieldErrors,
  focusFirstInvalid,
  isDefined,
  lineAmount,
  normalizePublicId,
  roundQuantity,
  toNumber
} from '@ims/utils';

type PoForm = FormGroup<{
  supplierId: PoHeaderGroup['controls']['supplierId'];
  warehouseId: PoHeaderGroup['controls']['warehouseId'];
  remark: PoHeaderGroup['controls']['remark'];
  lines: LinesArray;
}>;

/**
 * New or changed purchase order (a Draft). One screen for `/purchase-orders/new` and `/purchase-orders/:id/edit`: supplier, ship-to
 * warehouse (fixed once the order exists), lines found by product search with quantity, unit and price, and Save draft. Every save
 * sends the whole body (the API has no "absent = unchanged"); a stale 409 locks the form behind a Reload banner. Submitting is the
 * action bar of the detail page.
 */
@Component({
  selector: 'app-purchase-order-editor-page',
  imports: [
    AppMoneyPipe,
    ProductPickerComponent,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPublishHeightDirective,
    UiSelectComponent,
    UiSkeletonComponent,
    UiTextareaComponent
  ],
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './purchase-order-editor.component.html',
  styleUrl: './purchase-order-editor.component.scss'
})
export class PurchaseOrderEditorPageComponent implements HasUnsavedChanges {
  private readonly catalog = inject(CatalogStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private readonly pageTitle = inject(PageTitleService);
  private readonly partners = inject(PartnersStore);
  private readonly products = inject(ProductsStore);
  private readonly router = inject(Router);
  private readonly store = inject(PurchaseOrdersStore);
  private readonly toast = inject(ToastService);
  private readonly warehouses = inject(WarehousesStore);

  /** Route parameter; absent on `/new`. */
  $id = input<string | undefined>(undefined, { alias: 'id' });
  /**
   * The entry link of the reorder screen: `?warehouseId=<guid>&supplierId=<guid>&line=SKU-1|12&line=SKU-2|3`. Nothing in it is
   * trusted: a warehouse or supplier the person may not use, a product that is unknown or switched off, a bad quantity are ignored.
   */
  $warehouseParam = input<string | undefined>(undefined, { alias: 'warehouseId' });
  $supplierParam = input<string | undefined>(undefined, { alias: 'supplierId' });
  $lineParam = input<string | readonly string[] | undefined>(undefined, { alias: 'line' });

  readonly backLink = ['/', PURCHASE_ORDER_ROUTE];
  readonly form: PoForm = new FormGroup({
    ...createPoHeader().controls,
    lines: new FormArray<LineGroup>([])
  });
  readonly lines = this.form.controls.lines;

  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  $showLineError = signal(false);
  /** A save went through: the page is on its way to the order, and a second tap must not create another. */
  $saved = signal(false);
  readonly maxRemark = MAX_REMARK_LENGTH;

  readonly $values = toSignal(
    this.lines.valueChanges.pipe(
      startWith(null),
      map(() => this.lines.getRawValue())
    ),
    { initialValue: [] as LineValue[] }
  );
  readonly $totals = computed(() => computeTotals(this.$values(), DocumentType.Receipt));
  readonly $canAddLine = computed(() => this.$values().length < MAX_DOCUMENT_LINES);
  readonly $rows = computed(() =>
    this.$values().map((value, index) => ({
      index,
      group: this.lines.at(index),
      value,
      // The same gate as the total: a value the form refuses is left out.
      amount:
        isValidQuantity(value.qty) && isValidUnitCost(value.unitCost)
          ? lineAmount(toNumber(value.qty), toNumber(value.unitCost))
          : 0,
      units: value.uomOptions.map((option): SelectOption => ({
        value: option.uomId,
        label: option.code
      })),
      unitCode:
        value.uomOptions.find(option => option.uomId === value.uomId)?.code ?? value.baseUomCode
    }))
  );

  private readonly $orderId = computed(() => normalizePublicId(this.$id()));
  readonly $isEdit = computed(() => this.$id() !== undefined);
  private readonly $unusableId = computed(() => this.$isEdit() && this.$orderId() === undefined);
  readonly $order = computed(() => {
    const order = this.store.detail.$data();
    return order && order.id === this.$orderId() ? order : undefined;
  });
  readonly $notEditable = computed(() => {
    const order = this.$order();
    return order !== undefined && !toPoActions(order.allowedActions).has(PO_ACTION.Edit);
  });
  readonly $isLoading = computed(
    () => this.$isEdit() && this.$order() === undefined && this.store.detail.$isLoading()
  );
  readonly $notFound = computed(
    () =>
      this.$unusableId() || this.store.detail.$operation().error?.kind === API_ERROR_KIND.NotFound
  );
  readonly $loadFailed = computed(() => this.store.detail.$isFailed() && !this.$notFound());
  readonly $loadErrorMessage = computed(() => this.store.detail.$operation().error?.message);
  readonly $heading = computed(() =>
    this.$isEdit()
      ? (this.$order()?.no ?? this.i18n.t(this.$notFound() ? 'notFound.title' : 'ui.loading'))
      : this.i18n.t('po.new')
  );
  readonly $showForm = computed(
    () =>
      !this.$notFound() &&
      !this.$loadFailed() &&
      !this.$notEditable() &&
      (!this.$isEdit() || this.$order() !== undefined)
  );

  readonly $supplierOptions = computed<readonly SelectOption[]>(() => {
    const options = this.partners
      .$partners()
      .map(partner => ({ value: partner.id, label: partner.name }));
    const order = this.$order();
    // A supplier switched off since the order was made is still the order's supplier.
    return order && !options.some(option => option.value === order.supplierId)
      ? [...options, { value: order.supplierId, label: order.supplierName }]
      : options;
  });
  readonly $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );

  private readonly $saveError = computed(() => this.store.save.$operation().error);
  readonly $isStale = computed(() => isStaleOrderError(this.$saveError()));
  readonly $saveMessages = signal<readonly string[]>([]);
  readonly $isSaving = this.store.save.$isRunning;

  private readonly openPage = effect(() => {
    const id = this.$orderId();
    const edit = this.$isEdit();
    untracked(() => {
      // The store outlives this page and a sign-out: nothing of the last order or the last save is this page's.
      this.store.resetCommands();
      this.store.detail.reset();
      this.$saveMessages.set([]);
      this.$showLineError.set(false);
      this.$saved.set(false);
      // Order A's form is not order B's (also when B is not found).
      this.resetForm();
      if (edit && id !== undefined) {
        this.store.detail.load(id, handledInline());
      }
    });
  });

  private readonly fillFromOrder = effect(() => {
    const order = this.$order();
    if (!order || this.$notEditable()) {
      return;
    }
    untracked(() => {
      this.form.controls.warehouseId.disable();
      this.form.reset({ ...headerFromOrder(order), lines: [] });
      this.lines.clear();
      for (const line of order.lines) {
        this.lines.push(createLineGroup(lineFromOrderLine(line)));
      }
      this.markPristine();
    });
  });

  /** The order left Draft (or the caller may not edit it): the order page says where it stands. */
  private readonly leaveWhenNotEditable = effect(() => {
    const order = this.$order();
    if (order && this.$notEditable()) {
      void this.router.navigate(['/', PURCHASE_ORDER_ROUTE, order.id], { replaceUrl: true });
    }
  });

  /** The warehouse and supplier of the entry link, once their lists are in and only if they are in them (scope, active). */
  private readonly applyEntryLink = effect(() => {
    const warehouse = normalizePublicId(this.$warehouseParam());
    const supplier = normalizePublicId(this.$supplierParam());
    const inWarehouses = this.$warehouseOptions().some(option => option.value === warehouse);
    const inSuppliers = this.$supplierOptions().some(option => option.value === supplier);
    const { warehouseId, supplierId } = this.form.controls;
    untracked(() => {
      if (!this.$isEdit() && warehouse && inWarehouses && warehouseId.value === null) {
        warehouseId.setValue(warehouse);
      }
      if (!this.$isEdit() && supplier && inSuppliers && supplierId.value === null) {
        supplierId.setValue(supplier);
      }
    });
  });

  private readonly preselectWarehouse = effect(() => {
    const warehouses = this.warehouses.$activeWarehouses();
    if (
      !this.$isEdit() &&
      warehouses.length === 1 &&
      this.form.controls.warehouseId.value === null
    ) {
      untracked(() => this.form.controls.warehouseId.setValue(warehouses[0].id));
    }
  });

  private readonly showSaveErrors = effect(() => {
    const error = this.$saveError();
    untracked(() => {
      if (!error || isStaleOrderError(error)) {
        this.$saveMessages.set([]);
        return;
      }
      // The API names the price `unitPrice` (the shared form line calls it `unitCost`); an error on a whole line (`lines[0]`: a
      // product that is switched off, a unit that no longer converts) has no field: it is shown with the line's code.
      const fieldErrors: Record<string, readonly string[]> = {};
      const lineMessages: string[] = [];
      for (const [field, messages] of Object.entries(error.fieldErrors)) {
        const line = /^lines\[(\d+)\]$/.exec(field);
        if (line) {
          const sku = this.lines.at(Number(line[1]))?.controls.sku.value ?? '';
          lineMessages.push(...messages.map(message => `${sku}: ${message}`));
        } else {
          fieldErrors[field.replace(/\.unitPrice$/, '.unitCost')] = messages;
        }
      }
      const unmatched = applyFieldErrors(this.form, { ...error, fieldErrors });
      // The API's own sentence (the interceptor's `message` for a 403 is a general one).
      const refusal =
        error.kind === API_ERROR_KIND.Conflict || error.kind === API_ERROR_KIND.Forbidden
          ? [error.detail || error.message].filter(isDefined)
          : [];
      this.$saveMessages.set([...lineMessages, ...unmatched, ...refusal]);
    });
  });

  constructor() {
    // The two stores outlive this page and a sign-out: what another screen or user left in them (customers, inactive partners,
    // another warehouse scope) must not be what the entry link is checked against.
    this.partners.list.reset();
    this.warehouses.list.reset();
    this.partners.loadActive(PartnerType.Supplier);
    this.warehouses.ensureLoaded();
    this.destroyRef.onDestroy(() => this.pageTitle.setDetail(undefined));
    effect(() =>
      this.pageTitle.setDetail(this.$isEdit() ? this.$order()?.no : this.i18n.t('po.new'))
    );
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  /** A reload or a close of the tab with a half-written order asks first. */
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  onOpenAddItem(): void {
    this.$lastAdded.set(undefined);
    this.$addItemOpen.set(true);
  }

  /** Adds the product, or raises the quantity of the line it is already on. */
  onProductPicked(picked: PickedProduct): void {
    const added = this.addProduct(picked.product, 1);
    if (added !== undefined) {
      this.$lastAdded.set(
        this.i18n.t(added.increased ? 'doc.itemIncreased' : 'doc.itemAdded', {
          sku: picked.product.sku,
          qty: added.qty
        })
      );
    }
  }

  /** The user is searching for something else: the message about the last pick is out of date. */
  onPickerTermChanged(): void {
    this.$lastAdded.set(undefined);
  }

  onRemove(index: number): void {
    this.lines.removeAt(index);
    this.lines.markAsDirty();
    // The button that had the focus is gone: leave it on the way into the lines, not on the page.
    afterNextRender(
      () => this.page.querySelector<HTMLElement>('.po-editor-lines-header button')?.focus(),
      { injector: this.injector }
    );
  }

  onReload(): void {
    this.store.save.reset();
    this.store.detail.reload();
  }

  onRetryLoad(): void {
    this.store.detail.reload();
  }

  onSave(): void {
    this.form.markAllAsTouched();
    this.$showLineError.set(this.lines.length === 0);
    if (this.$saved() || this.form.invalid || this.lines.length === 0) {
      afterNextRender(() => focusFirstInvalid(this.page), { injector: this.injector });
      return;
    }
    const order = this.$order();
    this.store.save
      .run({
        ref: order ? toPurchaseOrderRef(order) : undefined,
        body: toSaveRequest(this.form.getRawValue(), this.lines.getRawValue())
      })
      .pipe(
        tap(saved => {
          this.markPristine();
          this.$saved.set(true);
          this.toast.success(this.i18n.t('doc.toast.draftSaved'));
          void this.router.navigate(['/', PURCHASE_ORDER_ROUTE, saved.id], { replaceUrl: true });
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private markPristine(): void {
    this.form.markAsPristine();
    this.lines.markAsPristine();
  }

  /** The line of this product gets `qty` more, or a new line starts at `qty`; undefined = the order is full. */
  private addProduct(
    product: Parameters<typeof lineFromProduct>[0],
    qty: number
  ): { increased: boolean; qty: number } | undefined {
    const existing = this.lines.controls.find(
      group => group.controls.productId.value === product.id
    );
    if (existing) {
      const next = roundQuantity((existing.controls.qty.value ?? 0) + qty);
      existing.controls.qty.setValue(next);
      existing.markAsDirty();
      return { increased: true, qty: next };
    }
    if (!this.$canAddLine()) {
      return undefined;
    }
    this.lines.push(
      createLineGroup({
        ...lineFromProduct(product, this.catalog.$unitCodes(), null),
        qty
      })
    );
    this.lines.markAsDirty();
    return { increased: false, qty };
  }

  /** An empty form (a single warehouse is pre-selected by an effect). */
  private resetForm(): void {
    this.form.controls.warehouseId.enable();
    this.form.reset({
      supplierId: null,
      warehouseId: null,
      remark: '',
      lines: []
    });
    this.lines.clear();
    this.markPristine();
    if (this.$isEdit()) {
      return;
    }
    // Lines of the entry link have a blank price (sent as 0.00; the box shows what is still to fill in).
    // One lookup at a time, in the order of the link; a failed one is skipped (the error interceptor has said it once).
    from(parsePrefillLines(this.$lineParam()))
      .pipe(
        concatMap(link =>
          this.products.lookup(link.sku).pipe(
            filter(result => result.found && result.product?.isActive === true),
            map(result => ({ product: result.product, qty: link.qty })),
            catchError(() => EMPTY)
          )
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(({ product, qty }) => product && this.addProduct(product, qty));
  }
}
