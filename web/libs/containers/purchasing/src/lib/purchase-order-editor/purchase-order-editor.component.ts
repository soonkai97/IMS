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
import { map, startWith, tap } from 'rxjs';
import { DocumentType, PartnerType } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { handledInline } from '@ims/core/http';
import { AppDatePipe, AppMoneyPipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { HasUnsavedChanges, PageTitleService } from '@ims/core/navigation';
import { ToastService } from '@ims/core/notification';
import { CatalogStore } from '@ims/features/catalog';
import {
  LineGroup,
  LineValue,
  LinesArray,
  computeTotals,
  createLineGroup,
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
  staleOrderDetails,
  toPoActions,
  toPurchaseOrderRef,
  toSaveRequest
} from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { API_ERROR_KIND, ApiError, MAX_DOCUMENT_LINES } from '@ims/models';
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
  toNumber
} from '@ims/utils';

type PoForm = FormGroup<{
  supplierId: PoHeaderGroup['controls']['supplierId'];
  warehouseId: PoHeaderGroup['controls']['warehouseId'];
  remark: PoHeaderGroup['controls']['remark'];
  lines: LinesArray;
}>;

/** The API names the price `unitPrice`; the form line (shared with the documents) calls it `unitCost`. */
function toFormFieldErrors(error: ApiError): ApiError {
  const fieldErrors: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    fieldErrors[field.replace(/\.unitPrice$/, '.unitCost')] = messages;
  }
  return { ...error, fieldErrors };
}

/**
 * New or changed purchase order (a Draft). One screen for `/purchase-orders/new` and `/purchase-orders/:id/edit`: supplier, ship-to
 * warehouse (fixed once the order exists), lines found by product search with quantity, unit and price, and Save draft. Every save
 * sends the whole body (the API has no "absent = unchanged"); a stale 409 locks the form behind a Reload banner. Submitting is the
 * action bar of the detail page.
 */
@Component({
  selector: 'app-purchase-order-editor-page',
  imports: [
    AppDatePipe,
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
  private readonly router = inject(Router);
  private readonly store = inject(PurchaseOrdersStore);
  private readonly toast = inject(ToastService);
  private readonly warehouses = inject(WarehousesStore);

  /** Route parameter; absent on `/new`. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly backLink = ['/', PURCHASE_ORDER_ROUTE];
  readonly form: PoForm = new FormGroup({
    ...createPoHeader().controls,
    lines: new FormArray<LineGroup>([])
  });
  readonly lines = this.form.controls.lines;

  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  $showLineError = signal(false);

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
      amount: lineAmount(toNumber(value.qty), toNumber(value.unitCost)),
      units: value.uomOptions.map((option): SelectOption => ({
        value: option.uomId,
        label: option.code
      })),
      unitCode:
        value.uomOptions.find(option => option.uomId === value.uomId)?.code ?? value.baseUomCode
    }))
  );

  /** The id in the URL; undefined on `/new` and when it is not a GUID (an old bookmark: the not-found page, no request). */
  private readonly $orderId = computed(() => normalizePublicId(this.$id()));
  readonly $isEdit = computed(() => this.$id() !== undefined);
  private readonly $unusableId = computed(() => this.$isEdit() && this.$orderId() === undefined);
  readonly $order = computed(() => {
    const order = this.store.detail.$data();
    return order && order.id === this.$orderId() ? order : undefined;
  });
  /** An order the API does not offer `edit` for (it left Draft, or the caller may not): no form, a way back. */
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
  /** Someone changed the order since it was opened: nothing is saved over it until it is read again. */
  readonly $isStale = computed(() => isStaleOrderError(this.$saveError()));
  readonly $staleDetails = computed(() => staleOrderDetails(this.$saveError()));
  /** What the form could not show at a field, and a refusal that is no field error (409, 403): the API's own words. */
  readonly $saveMessages = signal<readonly string[]>([]);
  readonly $isSaving = this.store.save.$isRunning;

  private readonly openPage = effect(() => {
    const id = this.$orderId();
    const edit = this.$isEdit();
    untracked(() => {
      // The store outlives this page and a sign-out: what the last save failed with, or the last order, is not this page's.
      this.store.resetCommands();
      this.store.detail.reset();
      this.$saveMessages.set([]);
      this.$showLineError.set(false);
      if (edit) {
        if (id !== undefined) {
          this.store.detail.load(id, handledInline());
        }
      } else {
        this.startNew();
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
      const unmatched = applyFieldErrors(this.form, toFormFieldErrors(error));
      const refusal =
        error.kind === API_ERROR_KIND.Conflict || error.kind === API_ERROR_KIND.Forbidden
          ? [error.message].filter(isDefined)
          : [];
      this.$saveMessages.set([...unmatched, ...refusal]);
    });
  });

  constructor() {
    this.partners.loadActive(PartnerType.Supplier);
    this.warehouses.ensureLoaded();
    this.destroyRef.onDestroy(() => this.pageTitle.setDetail(undefined));
    effect(() => this.pageTitle.setDetail(this.$order()?.no ?? this.i18n.t('po.new')));
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
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
    if (this.form.invalid || this.lines.length === 0) {
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
      const next = (existing.controls.qty.value ?? 0) + qty;
      existing.controls.qty.setValue(next);
      existing.markAsDirty();
      return { increased: true, qty: next };
    }
    if (!this.$canAddLine()) {
      return undefined;
    }
    this.lines.push(
      createLineGroup({ ...lineFromProduct(product, this.catalog.$unitCodes(), null), qty })
    );
    this.lines.markAsDirty();
    return { increased: false, qty };
  }

  /** An empty form (a single warehouse is pre-selected by an effect). */
  private startNew(): void {
    this.form.controls.warehouseId.enable();
    this.form.reset({
      supplierId: null,
      warehouseId: null,
      remark: '',
      lines: []
    });
    this.lines.clear();
    this.markPristine();
  }
}
