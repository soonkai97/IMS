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
import { map, startWith, switchMap, tap } from 'rxjs';
import { PartnerType } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { handledInline } from '@ims/core/http';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { HasUnsavedChanges, PageTitleService } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { CatalogStore } from '@ims/features/catalog';
import { buildUomOptions } from '@ims/features/documents';
import {
  MAX_PRICE_LIST_ITEMS,
  MAX_PRICE_LIST_NAME,
  PRICE_LIST_ROUTE,
  PriceItemGroup,
  PriceItemValue,
  PriceItemsArray,
  PriceListHeaderGroup,
  PriceListsStore,
  createPriceItemGroup,
  createPriceListHeader,
  duplicateItemIndexes,
  headerFromList,
  itemFromDto,
  toPriceListRef,
  toPriceListSaveRequest
} from '@ims/features/sales';
import { PartnersStore } from '@ims/features/warehousing';
import { API_ERROR_KIND, ApiError } from '@ims/models';
import {
  SelectOption,
  UiBannerComponent,
  UiButtonComponent,
  UiCheckboxComponent,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPublishHeightDirective,
  UiSearchInputComponent,
  UiSelectComponent,
  UiSkeletonComponent
} from '@ims/ui';
import {
  applyFieldErrors,
  controlSignal,
  debouncedSignal,
  focusFirstInvalid,
  isDefined,
  MONEY_DECIMALS,
  isStaleOrderError,
  normalizePublicId
} from '@ims/utils';

type PriceListForm = FormGroup<{
  name: PriceListHeaderGroup['controls']['name'];
  isDefault: PriceListHeaderGroup['controls']['isDefault'];
  customerId: PriceListHeaderGroup['controls']['customerId'];
  items: PriceItemsArray;
}>;

/**
 * New or changed price list. One screen for `/price-lists/new` and `/price-lists/:id`: name, default flag, the customer it is for,
 * and the prices (a product from the shared picker, the unit, the price). Every save sends the whole list with the version the
 * person opened; a stale 409 locks the form behind a Reload banner; the API's own sentence (a deactivated customer or product, a
 * second default, a customer who already has a list) is shown as it is. Opening the page forgets what the store holds of another
 * list or another user.
 */
@Component({
  selector: 'app-price-list-editor-page',
  imports: [
    ProductPickerComponent,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCheckboxComponent,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPublishHeightDirective,
    UiSearchInputComponent,
    UiSelectComponent,
    UiSkeletonComponent
  ],
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './price-list-editor.component.html',
  styleUrl: './price-list-editor.component.scss'
})
export class PriceListEditorPageComponent implements HasUnsavedChanges {
  private readonly catalog = inject(CatalogStore);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private readonly pageTitle = inject(PageTitleService);
  private readonly partners = inject(PartnersStore);
  private readonly router = inject(Router);
  private readonly store = inject(PriceListsStore);
  private readonly toast = inject(ToastService);

  /** Route parameter; absent on `/new`. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly backLink = ['/', PRICE_LIST_ROUTE];
  readonly form: PriceListForm = new FormGroup({
    ...createPriceListHeader().controls,
    items: new FormArray<PriceItemGroup>([])
  });
  readonly items = this.form.controls.items;
  readonly maxName = MAX_PRICE_LIST_NAME;
  readonly maxItems = MAX_PRICE_LIST_ITEMS;
  readonly priceStep = 10 ** -MONEY_DECIMALS;

  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  /** "SKU removed", for the screen reader (the row is gone and so is its text). */
  readonly $removedNote = signal('');
  /** A save went through: the page is on its way to the list, and a second tap must not create another. */
  $saved = signal(false);

  readonly $values = toSignal(
    this.items.valueChanges.pipe(
      startWith(null),
      map(() => this.items.getRawValue() as PriceItemValue[])
    ),
    { initialValue: [] as PriceItemValue[] }
  );
  readonly $canAddItem = computed(() => this.$values().length < MAX_PRICE_LIST_ITEMS);
  readonly $duplicates = computed(() => duplicateItemIndexes(this.$values()));
  /** A unit's choices, built once per row (the form keeps the same array while the person types). */
  private readonly unitChoices = new WeakMap<
    PriceItemValue['uomOptions'],
    readonly SelectOption[]
  >();
  private unitsOf(value: PriceItemValue): readonly SelectOption[] {
    let units = this.unitChoices.get(value.uomOptions);
    if (!units) {
      units = value.uomOptions.map((option): SelectOption => ({
        value: option.uomId,
        label: option.code
      }));
      this.unitChoices.set(value.uomOptions, units);
    }
    return units;
  }
  readonly $rows = computed(() =>
    this.$values().map((value, index) => ({
      index,
      group: this.items.at(index),
      value,
      units: this.unitsOf(value),
      unitCode: value.uomOptions.find(option => option.uomId === value.uomId)?.code ?? value.uomId,
      duplicate: this.$duplicates().has(index)
    }))
  );

  private readonly $listId = computed(() => normalizePublicId(this.$id()));
  readonly $isEdit = computed(() => this.$id() !== undefined);
  private readonly $unusableId = computed(() => this.$isEdit() && this.$listId() === undefined);
  readonly $list = computed(() => {
    const list = this.store.detail.$data();
    return list && list.id === this.$listId() ? list : undefined;
  });
  readonly $isLoading = computed(
    () => this.$isEdit() && this.$list() === undefined && this.store.detail.$isLoading()
  );
  readonly $notFound = computed(
    () =>
      this.$unusableId() || this.store.detail.$operation().error?.kind === API_ERROR_KIND.NotFound
  );
  readonly $loadFailed = computed(() => this.store.detail.$isFailed() && !this.$notFound());
  readonly $loadErrorMessage = computed(() => this.store.detail.$operation().error?.message);
  readonly $heading = computed(() =>
    this.$isEdit()
      ? (this.$list()?.name ?? this.i18n.t(this.$notFound() ? 'notFound.title' : 'ui.loading'))
      : this.i18n.t('pricelist.new')
  );
  readonly $showForm = computed(
    () =>
      !this.$notFound() && !this.$loadFailed() && (!this.$isEdit() || this.$list() !== undefined)
  );

  private readonly $isDefault = controlSignal(this.form.controls.isDefault);

  /** The list holds the first customers only (the API page size): above that a search box finds the rest. */
  readonly $customersTruncated = computed(() => {
    const page = this.partners.list.$data();
    return page !== undefined && page.total > page.items.length;
  });
  readonly $customerSearch = signal('');
  private readonly $customerTyped = debouncedSignal(this.$customerSearch);
  private lastCustomerSearch = '';
  /** Every customer seen so far, so the one already chosen stays in the select when a search narrows the list. */
  private readonly knownCustomers = new Map<string, string>();
  private readonly $customerId = controlSignal(this.form.controls.customerId);
  readonly $customerOptions = computed<readonly SelectOption[]>(() => {
    const options = this.partners
      .$partners()
      .map(partner => ({ value: partner.id, label: partner.name }));
    for (const option of options) {
      this.knownCustomers.set(option.value, option.label);
    }
    const list = this.$list();
    if (list?.customerId && list.customerName) {
      this.knownCustomers.set(list.customerId, list.customerName);
    }
    // A customer switched off since the list was made, or one chosen before a search, is still the chosen one.
    const chosen = this.$customerId();
    const chosenName = chosen === null ? undefined : this.knownCustomers.get(chosen);
    return chosen !== null && chosenName !== undefined && !options.some(o => o.value === chosen)
      ? [...options, { value: chosen, label: chosenName }]
      : options;
  });
  private readonly searchCustomers = effect(() => {
    const typed = this.$customerTyped().trim();
    untracked(() => {
      if (typed !== this.lastCustomerSearch) {
        this.lastCustomerSearch = typed;
        this.partners.loadActive(PartnerType.Customer, typed || undefined);
      }
    });
  });

  private readonly $saveError = computed(() => this.store.save.$operation().error);
  private readonly $deleteError = computed(() => this.store.delete.$operation().error);
  readonly $isStale = computed(
    () => isStaleOrderError(this.$saveError()) || isStaleOrderError(this.$deleteError())
  );
  readonly $messages = signal<readonly string[]>([]);
  readonly $isSaving = this.store.save.$isRunning;
  readonly $isBusy = computed(() => this.store.save.$isRunning() || this.store.delete.$isRunning());

  private readonly openPage = effect(() => {
    const id = this.$listId();
    const edit = this.$isEdit();
    untracked(() => {
      // The store outlives this page and a sign-out: nothing of the last list, the last save or the last user's refusal is this page's.
      this.store.resetCommands();
      this.store.detail.reset();
      this.$messages.set([]);
      this.$saved.set(false);
      // List A's form is not list B's (also when B is not found).
      this.resetForm();
      if (edit && id !== undefined) {
        this.store.detail.load(id, handledInline());
      }
    });
  });

  private readonly fillFromList = effect(() => {
    const list = this.$list();
    if (!list) {
      return;
    }
    untracked(() => {
      this.form.reset({ ...headerFromList(list), items: [] });
      this.items.clear();
      for (const item of list.items) {
        this.items.push(createPriceItemGroup(itemFromDto(item)));
      }
      this.markPristine();
    });
  });

  /** The default list has no customer: ticking the box clears and locks the customer. */
  private readonly lockCustomerForDefault = effect(() => {
    const isDefault = this.$isDefault();
    untracked(() => {
      const customer = this.form.controls.customerId;
      if (isDefault) {
        customer.setValue(null);
        customer.disable({ emitEvent: false });
      } else {
        customer.enable({ emitEvent: false });
      }
    });
  });

  private readonly showErrors = effect(() => {
    const error = this.$saveError() ?? this.$deleteError();
    untracked(() => {
      if (!error || isStaleOrderError(error)) {
        this.$messages.set([]);
        return;
      }
      this.$messages.set(this.messagesOf(error));
      afterNextRender(() => focusFirstInvalid(this.page), { injector: this.injector });
    });
  });

  constructor() {
    // The two stores outlive this page and a sign-out: what another screen or user left in them (another customer list) must not be
    // what the picker offers.
    this.partners.list.reset();
    this.partners.loadActive(PartnerType.Customer);
    this.destroyRef.onDestroy(() => this.pageTitle.setDetail(undefined));
    effect(() =>
      this.pageTitle.setDetail(this.$isEdit() ? this.$list()?.name : this.i18n.t('pricelist.new'))
    );
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  /** A reload or a close of the tab with a half-written list asks first. */
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  onOpenAddItem(): void {
    this.$lastAdded.set(undefined);
    this.$addItemOpen.set(true);
  }

  /** Adds the product in its base unit, or says it is already on the list. */
  onProductPicked(picked: PickedProduct): void {
    this.$removedNote.set('');
    const product = picked.product;
    const exists = this.items.controls.some(
      group =>
        group.controls.productId.value === product.id &&
        group.controls.uomId.value === product.baseUomId
    );
    if (exists) {
      this.$lastAdded.set(this.i18n.t('pricelist.itemExists', { sku: product.sku }));
      return;
    }
    if (!this.$canAddItem()) {
      this.$lastAdded.set(this.i18n.t('pricelist.itemsFull', { max: MAX_PRICE_LIST_ITEMS }));
      return;
    }
    this.items.push(
      createPriceItemGroup({
        productId: product.id,
        sku: product.sku,
        productName: product.name,
        uomId: product.baseUomId,
        uomOptions: buildUomOptions(product, this.catalog.$unitCodes()),
        price: null
      })
    );
    this.items.markAsDirty();
    this.$lastAdded.set(this.i18n.t('pricelist.itemAdded', { sku: product.sku }));
  }

  /** The user is searching for something else: the message about the last pick is out of date. */
  onPickerTermChanged(): void {
    this.$lastAdded.set(undefined);
  }

  onRemove(index: number): void {
    const sku = this.items.at(index)?.controls.sku.value ?? '';
    this.$removedNote.set(this.i18n.t('pricelist.itemRemoved', { sku }));
    this.items.removeAt(index);
    this.items.markAsDirty();
    // The button that had the focus is gone: leave it on the way into the prices, not on the page.
    afterNextRender(
      () => this.page.querySelector<HTMLElement>('.price-list-editor-items-header button')?.focus(),
      { injector: this.injector }
    );
  }

  onReload(): void {
    this.store.resetCommands();
    this.store.detail.reload();
  }

  onRetryLoad(): void {
    this.store.detail.reload();
  }

  onSave(): void {
    this.form.markAllAsTouched();
    if (this.$saved() || this.form.invalid || this.$duplicates().size > 0) {
      afterNextRender(() => this.focusFirstProblem(), { injector: this.injector });
      return;
    }
    const list = this.$list();
    this.store.save
      .run({
        ref: list ? toPriceListRef(list) : undefined,
        body: toPriceListSaveRequest(
          this.form.getRawValue(),
          this.items.getRawValue() as PriceItemValue[]
        )
      })
      .pipe(
        tap(() => {
          this.markPristine();
          this.$saved.set(true);
          this.toast.success(this.i18n.t('pricelist.toast.saved'));
          void this.router.navigate(['/', PRICE_LIST_ROUTE], { replaceUrl: true });
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Delete asks first, then sends the version the person opened; the list page follows. */
  onDelete(): void {
    const list = this.$list();
    if (!list) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('pricelist.delete.title', { name: list.name }),
        body: this.i18n.t('pricelist.delete.body'),
        confirmLabel: this.i18n.t('pricelist.delete'),
        cancelLabel: this.i18n.t('pricelist.delete.keep'),
        destructive: true
      })
      .pipe(
        switchMap(() => this.store.delete.run(toPriceListRef(list))),
        tap(() => {
          this.markPristine();
          this.$saved.set(true);
          this.toast.success(this.i18n.t('pricelist.toast.deleted'));
          void this.router.navigate(['/', PRICE_LIST_ROUTE], { replaceUrl: true });
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /**
   * The API's field messages go to their fields (`items[0].price`); a message about a whole item (`items[0]`: a product switched
   * off, a unit that no longer converts) has no field and is shown with the item's code; a refusal (403, 409) is the API's own
   * sentence (a deactivated customer, a second default list, a customer who already has one), not the interceptor's general one.
   */
  private messagesOf(error: ApiError): string[] {
    const itemMessages: string[] = [];
    const fieldErrors: Record<string, readonly string[]> = {};
    for (const [field, messages] of Object.entries(error.fieldErrors)) {
      const item = /^items\[(\d+)\]$/.exec(field);
      if (item) {
        const sku = this.items.at(Number(item[1]))?.controls.sku.value ?? '';
        itemMessages.push(...messages.map(message => `${sku}: ${message}`));
      } else {
        fieldErrors[field] = messages;
      }
    }
    const unmatched = applyFieldErrors(this.form, { ...error, fieldErrors });
    const refusal =
      error.kind === API_ERROR_KIND.Conflict || error.kind === API_ERROR_KIND.Forbidden
        ? [error.detail || error.message].filter(isDefined)
        : [];
    return [...itemMessages, ...unmatched, ...refusal];
  }

  /** The first field in error, else the first repeated price (its sentence takes the focus). */
  private focusFirstProblem(): void {
    if (!focusFirstInvalid(this.page)) {
      this.page.querySelector<HTMLElement>('.price-list-editor-duplicate')?.focus();
    }
  }

  private markPristine(): void {
    this.form.markAsPristine();
    this.items.markAsPristine();
  }

  /** An empty form. */
  private resetForm(): void {
    this.form.reset({ name: '', isDefault: false, customerId: null, items: [] });
    this.items.clear();
    this.markPristine();
  }
}
