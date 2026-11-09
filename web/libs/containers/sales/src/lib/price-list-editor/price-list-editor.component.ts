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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize, switchMap, tap } from 'rxjs';
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
import { API_ERROR_KIND, ApiError, MAX_UNIT_COST } from '@ims/models';
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
  UiPagerComponent,
  UiPublishHeightDirective,
  UiSearchInputComponent,
  UiSelectComponent,
  UiSkeletonComponent
} from '@ims/ui';
import {
  MONEY_DECIMALS,
  SERVER_ERROR_KEY,
  applyFieldErrors,
  controlSignal,
  debouncedSignal,
  focusFirstInvalid,
  isDefined,
  isStaleOrderError,
  normalizePublicId
} from '@ims/utils';

type PriceListForm = FormGroup<{
  name: PriceListHeaderGroup['controls']['name'];
  isDefault: PriceListHeaderGroup['controls']['isDefault'];
  customerId: PriceListHeaderGroup['controls']['customerId'];
  items: PriceItemsArray;
}>;

/** One message of the API about a field of a price: its position, the field's path in the price, the sentence. */
interface ItemError {
  readonly index: number;
  readonly path: string;
  readonly message: string;
}

/** How many prices are on screen at once: 2000 rows of inputs would be about 10,000 components, and every key would redraw them. */
export const PRICES_PER_PAGE = 50;

/**
 * New or changed price list. One screen for `/price-lists/new` and `/price-lists/:id`: name, default flag, the customer it is for,
 * and the prices (a product from the shared picker, the unit, the price), shown 50 at a time. Every save sends the whole list with
 * the version the person opened; a stale 409 locks the form behind a Reload banner; the API's own sentence (a deactivated customer
 * or product, a second default, a customer who already has a list) is shown as it is. Opening the page forgets what the store holds
 * of another list or another user.
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
    UiPagerComponent,
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
  readonly maxPrice = MAX_UNIT_COST;
  readonly perPage = PRICES_PER_PAGE;
  readonly priceStep = 10 ** -MONEY_DECIMALS;

  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  /** "SKU removed", for the screen reader (the row is gone and so is its text). */
  readonly $removedNote = signal('');
  /** A save or a delete went through (or is on its way): the page is leaving, and a second tap must not repeat it. */
  $saved = signal(false);
  /** The delete dialog is open: a second tap on the button opens no second one. */
  private readonly $asking = signal(false);

  /** Bumped by every change of the items; what reads it is cheap (a count, the product and unit keys, the 50 rows on screen). */
  private readonly $changes = signal(0);
  private readonly watchItems = this.items.valueChanges
    .pipe(takeUntilDestroyed())
    .subscribe(() => this.$changes.update(count => count + 1));
  readonly $count = computed(() => {
    this.$changes();
    return this.items.length;
  });
  readonly $canAddItem = computed(() => this.$count() < MAX_PRICE_LIST_ITEMS);
  /** Positions of items that repeat a product and unit above them, with the position of the first. */
  readonly $duplicates = computed(() => {
    this.$changes();
    return duplicateItemIndexes(
      this.items.controls.map(group => ({
        productId: group.controls.productId.value,
        uomId: group.controls.uomId.value
      }))
    );
  });

  /** The row number (from 1) of the first price a repeated one repeats, by the position of the repeat. */
  readonly $repeatOf = computed(
    () => new Map([...this.$duplicates()].map(([repeat, first]) => [repeat, first + 1] as const))
  );

  readonly $pageNumber = signal(1);
  readonly $pageCount = computed(() => Math.max(1, Math.ceil(this.$count() / PRICES_PER_PAGE)));
  /** The page on screen, never past the last one (items were removed). */
  readonly $currentPage = computed(() => Math.min(this.$pageNumber(), this.$pageCount()));

  /** A unit's choices, built once per row (the form keeps the same array while the person types). */
  private readonly unitChoices = new WeakMap<
    PriceItemValue['uomOptions'],
    readonly SelectOption[]
  >();
  private unitsOf(options: PriceItemValue['uomOptions']): readonly SelectOption[] {
    let units = this.unitChoices.get(options);
    if (!units) {
      units = options.map((option): SelectOption => ({ value: option.uomId, label: option.code }));
      this.unitChoices.set(options, units);
    }
    return units;
  }
  /** Only the rows of the page on screen are built (50 at most): typing in a price touches them, not all 2000. */
  readonly $rows = computed(() => {
    this.$changes();
    const start = (this.$currentPage() - 1) * PRICES_PER_PAGE;
    return this.items.controls.slice(start, start + PRICES_PER_PAGE).map((group, offset) => {
      const options = group.controls.uomOptions.value;
      const uomId = group.controls.uomId.value;
      return {
        index: start + offset,
        group,
        sku: group.controls.sku.value,
        productName: group.controls.productName.value,
        units: this.unitsOf(options),
        unitCode: options.find(option => option.uomId === uomId)?.code ?? uomId
      };
    });
  });

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
  private readonly $knownCustomers = signal<ReadonlyMap<string, string>>(new Map());
  private readonly rememberCustomers = effect(() => {
    const seen = this.partners.$partners().map(partner => [partner.id, partner.name] as const);
    const list = this.$list();
    untracked(() => {
      const known = new Map(this.$knownCustomers());
      seen.forEach(([id, name]) => known.set(id, name));
      if (list?.customerId && list.customerName) {
        known.set(list.customerId, list.customerName);
      }
      this.$knownCustomers.set(known);
    });
  });
  private readonly $customerId = controlSignal(this.form.controls.customerId);
  readonly $customerOptions = computed<readonly SelectOption[]>(() => {
    const options = this.partners
      .$partners()
      .map(partner => ({ value: partner.id, label: partner.name }));
    // A customer switched off since the list was made, or one chosen before a search, is still the chosen one.
    const chosen = this.$customerId();
    const chosenName = chosen === null ? undefined : this.$knownCustomers().get(chosen);
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
  readonly $isBusy = computed(
    () => this.store.save.$isRunning() || this.store.delete.$isRunning() || this.$asking()
  );

  private readonly openPage = effect(() => {
    const id = this.$listId();
    const edit = this.$isEdit();
    untracked(() => {
      // The store outlives this page and a sign-out: nothing of the last list, the last save or the last user's refusal is this page's.
      this.store.resetCommands();
      this.store.detail.reset();
      this.$messages.set([]);
      this.$saved.set(false);
      this.$pageNumber.set(1);
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
      // A save in flight brings the saved list back as the opened one: the page is leaving, so its rows are not built again.
      if (this.$saved()) {
        return;
      }
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
      const { messages, firstRow, itemErrors } = this.messagesOf(error);
      this.$messages.set(messages);
      if (firstRow !== undefined) {
        const before = this.$currentPage();
        this.showRow(firstRow);
        if (this.$currentPage() !== before) {
          // The rows of another page are built by the next render; their fields learn of the errors only when told again.
          afterNextRender(
            () => {
              this.markItemErrors(itemErrors);
              afterNextRender(() => this.focusFirstProblem(), { injector: this.injector });
            },
            { injector: this.injector }
          );
          return;
        }
      }
      afterNextRender(() => this.focusFirstProblem(), { injector: this.injector });
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

  /**
   * Adds the product in the first of its units that has no price yet (its base unit first), or says it is priced in every unit.
   * The new price is on the last page, where the person finds it.
   */
  onProductPicked(picked: PickedProduct): void {
    this.$removedNote.set('');
    const product = picked.product;
    const options = buildUomOptions(product, this.catalog.$unitCodes());
    const used = new Set(
      this.items.controls
        .filter(group => group.controls.productId.value === product.id)
        .map(group => group.controls.uomId.value)
    );
    const unit = options.find(option => !used.has(option.uomId));
    if (unit === undefined) {
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
        uomId: unit.uomId,
        uomOptions: options,
        price: null
      })
    );
    this.items.markAsDirty();
    this.showRow(this.items.length - 1);
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

  onPageChange(page: number): void {
    this.$pageNumber.set(page);
  }

  /** Stops the mouse wheel from changing the price under the pointer. */
  onWheel(event: Event): void {
    if (event.target instanceof HTMLInputElement && this.page.activeElement === event.target) {
      event.target.blur();
    }
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
    const duplicates = this.$duplicates();
    if (this.$saved() || this.form.invalid || duplicates.size > 0) {
      // The header first; else the page of the first price in trouble, so its field or sentence is on screen to take the focus.
      const header = this.form.controls.name.invalid || this.form.controls.customerId.invalid;
      const problem = this.firstProblemRow();
      if (!header && problem !== undefined) {
        this.showRow(problem);
      }
      afterNextRender(() => this.focusFirstProblem(), { injector: this.injector });
      return;
    }
    const list = this.$list();
    let succeeded = false;
    this.$saved.set(true);
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
          succeeded = true;
          this.markPristine();
          this.toast.success(this.i18n.t('pricelist.toast.saved'));
          void this.router.navigate(['/', PRICE_LIST_ROUTE], { replaceUrl: true });
        }),
        finalize(() => {
          if (!succeeded) {
            this.$saved.set(false);
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Delete asks first (once, however often the button is tapped), then sends the version the person opened; the list page follows. */
  onDelete(): void {
    const list = this.$list();
    if (!list || this.$asking() || this.$saved()) {
      return;
    }
    this.$asking.set(true);
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
        finalize(() => this.$asking.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /**
   * What the API said, in words. A message about a price (`items[3].price`, or a whole item `items[3]`: a product switched off, a
   * unit that no longer converts) goes on that row's field when there is one and is also listed with the product's code, so it is
   * found whichever page is on screen; the other field messages go to their fields; a refusal (403, 409) is the API's own sentence
   * (a deactivated customer, a second default list, a customer who already has one), not the interceptor's general one.
   */
  private messagesOf(error: ApiError): {
    messages: string[];
    firstRow: number | undefined;
    itemErrors: ItemError[];
  } {
    const itemMessages: string[] = [];
    const itemErrors: ItemError[] = [];
    const fieldErrors: Record<string, readonly string[]> = {};
    let firstRow: number | undefined;
    for (const [field, messages] of Object.entries(error.fieldErrors)) {
      const item = /^items\[(\d+)\](?:\.(.+))?$/.exec(field);
      if (!item) {
        fieldErrors[field] = messages;
        continue;
      }
      const index = Number(item[1]);
      const group = this.items.at(index);
      if (item[2]) {
        itemErrors.push({ index, path: item[2], message: messages[0] });
      }
      const sku = group?.controls.sku.value ?? '';
      itemMessages.push(...messages.map(message => `${sku}: ${message}`));
      firstRow = firstRow === undefined ? index : Math.min(firstRow, index);
    }
    const unmatched = applyFieldErrors(this.form, { ...error, fieldErrors });
    const refusal =
      error.kind === API_ERROR_KIND.Conflict || error.kind === API_ERROR_KIND.Forbidden
        ? [error.detail || error.message].filter(isDefined)
        : [];
    this.markItemErrors(itemErrors);
    return { messages: [...itemMessages, ...unmatched, ...refusal], firstRow, itemErrors };
  }

  /** Puts the API's message on the field of each price it named (the field exists only while its page is on screen). */
  private markItemErrors(errors: readonly ItemError[]): void {
    for (const { index, path, message } of errors) {
      const control = this.items.at(index)?.get(path);
      if (control) {
        control.setErrors({ [SERVER_ERROR_KEY]: message });
        control.markAsTouched();
      }
    }
  }

  /** The first price that is wrong (a field the form refuses, or a repeat), by position. */
  private firstProblemRow(): number | undefined {
    const invalid = this.items.controls.findIndex(group => group.invalid);
    const repeated = Math.min(...this.$duplicates().keys());
    const found = [invalid, repeated].filter(index => index >= 0 && Number.isFinite(index));
    return found.length > 0 ? Math.min(...found) : undefined;
  }

  /** Puts the page that holds the price on screen. */
  private showRow(index: number): void {
    this.$pageNumber.set(Math.floor(index / PRICES_PER_PAGE) + 1);
  }

  /**
   * The first field in error takes the focus; else the first repeated price (its sentence); else the first alert, which tells
   * what the API refused (a refusal names no field, and the focus would otherwise stay on Save in the bar below).
   */
  private focusFirstProblem(): void {
    if (focusFirstInvalid(this.page)) {
      return;
    }
    const target =
      this.page.querySelector<HTMLElement>('.price-list-editor-duplicate') ??
      this.page.querySelector<HTMLElement>('app-banner[role="alert"]');
    target?.focus();
    target?.scrollIntoView?.({ block: 'center' });
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
