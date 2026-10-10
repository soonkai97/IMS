import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, filter, finalize, of, switchMap, tap } from 'rxjs';
import {
  DocumentDto,
  DocumentStatus,
  DocumentType,
  IssuePurpose,
  LedgerRow,
  PartnerType
} from '@ims/api';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_STATUS_LABELS,
  I18nService,
  ISSUE_PURPOSE_LABELS,
  MessageKey,
  TranslatePipe
} from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { HasUnsavedChanges, PageTitleService } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { SystemSettingsStore } from '@ims/core/settings';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import {
  DOCUMENT_ROUTE,
  DEFAULT_QTY,
  DocumentsStore,
  LineGroup,
  NEW_DOCUMENT_ID,
  STATUS_TONE,
  isReversalBlockedError,
  isStaleDocumentError,
  isStockConflictError,
  reversalShortages,
  staleDetails,
  toDocumentRef
} from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  DOCUMENT_ACTION,
  DocumentAction,
  MIN_REASON_LENGTH,
  PERMISSION,
  toDocumentActions
} from '@ims/models';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import {
  RadioOption,
  SelectOption,
  TabItem,
  TableColumn,
  UiBannerComponent,
  UiButtonComponent,
  UiCellDirective,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiRadioGroupComponent,
  UiSelectComponent,
  UiSkeletonComponent,
  UiStatusChipComponent,
  UiTableComponent,
  UiTabsComponent,
  UiTextareaComponent
} from '@ims/ui';
import { applyFieldErrors, controlSignal, isDefined } from '@ims/utils';
import { DocumentActionsComponent } from '../document-actions/document-actions.component';
import { DocumentLineEditorComponent } from '../line-editor/line-editor.component';
import { DocumentEditorViewService } from './document-editor.view-service';

const TAB_ITEMS = 'items';
const TAB_HISTORY = 'history';
const TAB_LEDGER = 'ledger';
const LEDGER_PAGE_SIZE = 100;
/** How many lines the Post dialog names; the rest is "and N more". */
const POST_SUMMARY_LINES = 3;

interface StatusHint {
  readonly key: MessageKey;
  readonly name?: string;
  readonly at?: string;
}

interface StockConflict {
  readonly sku: string;
  readonly available: number;
  readonly requested: number;
  readonly uom: string;
}

interface HistoryEvent {
  readonly labelKey: MessageKey;
  readonly by?: string;
  readonly at?: string;
  readonly reason?: string;
}

/**
 * One editor for receipts and issues (route data `type`). Buttons come from the server's `allowedActions`; the
 * page never re-implements who may do what (D3). New and Draft documents are editable, everything else is read-only.
 */
@Component({
  selector: 'app-document-editor-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
    DocumentActionsComponent,
    DocumentLineEditorComponent,
    ProductPickerComponent,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCellDirective,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiRadioGroupComponent,
    UiSelectComponent,
    UiSkeletonComponent,
    UiStatusChipComponent,
    UiTableComponent,
    UiTabsComponent,
    UiTextareaComponent
  ],
  providers: [DocumentEditorViewService],
  host: {
    '(document:visibilitychange)': 'onVisibilityChange()',
    '(window:beforeunload)': 'onBeforeUnload($event)'
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-editor.component.html',
  styleUrl: './document-editor.component.scss'
})
export class DocumentEditorPageComponent implements OnInit, HasUnsavedChanges {
  private readonly catalog = inject(CatalogStore);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly documents = inject(DocumentsStore);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private readonly pageTitle = inject(PageTitleService);
  private readonly partners = inject(PartnersStore);
  private readonly permissions = inject(PermissionService);
  private readonly products = inject(ProductsStore);
  private readonly router = inject(Router);
  private readonly settings = inject(SystemSettingsStore);
  private readonly stock = inject(StockStore);
  private readonly toast = inject(ToastService);
  private readonly warehouses = inject(WarehousesStore);
  readonly view = inject(DocumentEditorViewService);

  /** Route data. */
  $type = input.required<DocumentType>({ alias: 'type' });
  /** Route parameter; absent on /new. */
  $id = input<string | undefined>(undefined, { alias: 'id' });
  /** Query parameters of a link that starts a document for a product (Low stock: Create receipt). */
  $sku = input<string | undefined>(undefined, { alias: 'sku' });
  $warehouseParam = input<string | undefined>(undefined, { alias: 'warehouseId' });

  /** Id of the document this editor just created: the store already holds it, so the route change needs no reload. */
  private justCreatedId: number | undefined;

  $tab = signal<string>(TAB_ITEMS);
  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  $showLineError = signal(false);

  /** The id in the URL; undefined on `/new`. */
  private readonly $routeDocumentId = computed(() => {
    const id = this.$id();
    return id === undefined || id === NEW_DOCUMENT_ID ? undefined : Number(id);
  });
  /**
   * Set the moment a new document is created. The editor is bound to the saved document from then on (so a repeated
   * Save or Submit updates it instead of creating a second one) even before the router has moved `/new` to `/<id>`.
   */
  private readonly $createdId = signal<number | undefined>(undefined);
  readonly $documentId = computed(() => this.$routeDocumentId() ?? this.$createdId());
  readonly $isNew = computed(() => this.$documentId() === undefined);
  readonly $isIssue = computed(() => this.$type() === DocumentType.Issue);
  readonly $document = computed<DocumentDto | undefined>(() => {
    const document = this.documents.detail.$data();
    return document && document.id === this.$documentId() ? document : undefined;
  });
  /** A brand-new document can be saved and submitted; an opened one offers what the server allows. */
  readonly $allowedActions = computed<readonly string[]>(() =>
    this.$isNew()
      ? [DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit]
      : (this.$document()?.allowedActions ?? [])
  );
  private readonly $actions = computed(() => toDocumentActions(this.$allowedActions()));
  readonly $editable = computed(() => this.$actions().has(DOCUMENT_ACTION.Edit));
  readonly $status = computed(() => this.$document()?.status ?? DocumentStatus.Draft);
  readonly $statusTone = computed(() => STATUS_TONE[this.$status()]);
  readonly $statusLabel = computed(() =>
    this.i18n.enumLabel(DOCUMENT_STATUS_LABELS, this.$status())
  );
  readonly $isLoading = computed(
    () => this.documents.detail.$isLoading() && this.$document() === undefined
  );
  readonly $loadFailed = this.documents.detail.$isFailed;
  /** The id in the URL belongs to no document (an old bookmark): a page of its own, and retrying cannot help. */
  readonly $notFound = computed(
    () => this.documents.detail.$operation().error?.kind === API_ERROR_KIND.NotFound
  );
  readonly $canPost = computed(() => this.permissions.has(PERMISSION.DocumentPost));
  /** The button that was tapped shows the spinner, not whichever button is primary (NV-21). */
  private readonly $activeAction = signal<DocumentAction | undefined>(undefined);
  readonly $busyAction = computed(() => (this.$isBusy() ? this.$activeAction() : undefined));
  readonly $isBusy = computed(
    () =>
      this.documents.save.$isRunning() ||
      this.documents.submit.$isRunning() ||
      this.documents.approve.$isRunning() ||
      this.documents.reject.$isRunning() ||
      this.documents.cancel.$isRunning() ||
      this.documents.post.$isRunning() ||
      this.documents.reverse.$isRunning()
  );
  readonly $selfApprovalBlocked = computed(() => {
    const document = this.$document();
    return (
      document !== undefined &&
      document.status === DocumentStatus.Submitted &&
      document.requiresApproval &&
      document.submittedById === this.permissions.$userId() &&
      this.permissions.has(PERMISSION.DocumentApprove) &&
      !this.$actions().has(DOCUMENT_ACTION.Approve)
    );
  });

  readonly $warehouseId = controlSignal(this.view.header.controls.warehouseId);
  readonly $totals = this.view.$totals;
  readonly $values = this.view.$values;
  readonly $allowNegativeStock = this.settings.$allowNegativeStock;
  readonly $linesBlocked = computed(
    () => this.$totals().shortLineCount > 0 && !this.$allowNegativeStock()
  );
  readonly $needsOverride = computed(
    () => this.$totals().shortLineCount > 0 && this.$allowNegativeStock()
  );
  readonly $awaitingApproval = computed(
    () => this.$status() === DocumentStatus.Submitted && this.$document()?.requiresApproval === true
  );
  /** An issue needs its warehouse first: the picker shows stock of that warehouse. */
  readonly $canAddItem = computed(
    () => this.view.$canAddLine() && (!this.$isIssue() || this.$warehouseId() !== null)
  );
  readonly $noLines = computed(() => this.$values().length === 0);
  readonly $warehouseName = computed(() => {
    const id = this.$warehouseId();
    return this.warehouses.$warehouses().find(warehouse => warehouse.id === id)?.name ?? '';
  });
  readonly $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );
  readonly $partnerOptions = computed<readonly SelectOption[]>(() =>
    this.partners.$partners().map(partner => ({ value: partner.id, label: partner.name }))
  );
  readonly $purposeOptions = computed<readonly RadioOption[]>(() => [
    { value: IssuePurpose.Sale, label: this.i18n.t('purpose.Sale') },
    { value: IssuePurpose.Use, label: this.i18n.t('purpose.Use') },
    { value: IssuePurpose.Scrap, label: this.i18n.t('purpose.Scrap') }
  ]);
  readonly $backLink = computed(() => ['/', DOCUMENT_ROUTE[this.$type()]]);
  readonly $backLabel = computed(() =>
    this.i18n.t(this.$isIssue() ? 'issues.title' : 'receipts.title')
  );
  readonly $heading = computed(() => {
    const document = this.$document();
    if (document) {
      return document.no;
    }
    if (this.$isNew()) {
      return this.i18n.t(this.$isIssue() ? 'issues.new' : 'receipts.new');
    }
    // An opened document that has not arrived yet is not "New receipt" (PS-20).
    return this.i18n.t(this.$notFound() ? 'notFound.title' : 'ui.loading');
  });
  readonly $partnerLabelKey = computed<MessageKey>(() =>
    this.$isIssue() ? 'doc.customer' : 'doc.supplier'
  );

  /** A Draft that a reviewer sent back: the banner says who and why. */
  readonly $returned = computed(() => {
    const document = this.$document();
    return document?.isReturned ? document : undefined;
  });
  readonly $returnedBy = computed(() => {
    const document = this.$returned();
    return document ? (document.returnedByName ?? document.returnedBy ?? '') : '';
  });
  readonly $purposeText = computed(() =>
    this.i18n.enumLabel(ISSUE_PURPOSE_LABELS, this.$document()?.purpose)
  );
  readonly $tabs = computed<readonly TabItem[]>(() => {
    const tabs: TabItem[] = [
      { id: TAB_ITEMS, label: this.i18n.t('doc.tab.items') },
      { id: TAB_HISTORY, label: this.i18n.t('doc.tab.history') }
    ];
    const status = this.$status();
    if (status === DocumentStatus.Posted || status === DocumentStatus.Reversed) {
      tabs.push({ id: TAB_LEDGER, label: this.i18n.t('doc.tab.ledger') });
    }
    return tabs;
  });
  readonly $isItemsTab = computed(() => this.$tab() === TAB_ITEMS);
  readonly $isHistoryTab = computed(() => this.$tab() === TAB_HISTORY);
  readonly $isLedgerTab = computed(() => this.$tab() === TAB_LEDGER);
  readonly $history = computed<readonly HistoryEvent[]>(() => {
    const document = this.$document();
    if (!document) {
      return [];
    }
    const events: (HistoryEvent | undefined)[] = [
      { labelKey: 'doc.history.created', by: document.createdByName, at: document.createdAt },
      document.submittedAt
        ? {
            labelKey: 'doc.history.submitted',
            by: document.submittedByName ?? document.submittedBy,
            at: document.submittedAt
          }
        : undefined,
      document.rejectionReason
        ? {
            labelKey: 'doc.history.returned',
            by: document.returnedByName ?? document.returnedBy,
            at: document.returnedAt,
            reason: document.rejectionReason
          }
        : undefined,
      document.approvedAt
        ? {
            labelKey: 'doc.history.approved',
            by: document.approvedByName ?? document.approvedBy,
            at: document.approvedAt
          }
        : undefined,
      document.negativeStockOverrideReason
        ? { labelKey: 'doc.history.override', reason: document.negativeStockOverrideReason }
        : undefined,
      document.postedAt
        ? {
            labelKey: 'doc.history.posted',
            by: document.postedByName ?? document.postedBy,
            at: document.postedAt
          }
        : undefined,
      document.reversalReason
        ? { labelKey: 'doc.history.reversed', reason: document.reversalReason }
        : undefined,
      document.cancelReason
        ? { labelKey: 'doc.history.cancelled', reason: document.cancelReason }
        : undefined
    ];
    return events.filter(isDefined);
  });
  readonly $ledgerRows = computed(() => this.stock.ledger.$data()?.items ?? []);
  readonly $ledgerLoading = this.stock.ledger.$isLoading;
  readonly $ledgerColumns = computed<readonly TableColumn<LedgerRow>[]>(() => [
    { key: 'sku', header: this.i18n.t('product.sku'), value: row => row.sku, primary: true },
    { key: 'name', header: this.i18n.t('product.name'), value: row => row.productName },
    { key: 'qty', header: this.i18n.t('ledger.qtyChange'), align: 'end' },
    {
      key: 'warehouse',
      header: this.i18n.t('stock.warehouse'),
      value: row => row.warehouseName,
      hideOnPhone: true
    }
  ]);
  /**
   * The last error of each document command, newest state only: a command that succeeded later clears its own, and any
   * command that changes the document clears the others' (the store does that), so a stale banner cannot outlive the
   * state it described (NV-06).
   */
  private readonly $postingErrors = computed(() => [
    this.documents.save.$operation().error,
    this.documents.submit.$operation().error,
    this.documents.approve.$operation().error,
    this.documents.reject.$operation().error,
    this.documents.cancel.$operation().error,
    this.documents.post.$operation().error
  ]);
  private readonly $commandErrors = computed(() => [
    ...this.$postingErrors(),
    this.documents.reverse.$operation().error
  ]);
  /**
   * Token the server held when the user came back to this tab, and the token of the opened copy it was compared with.
   * A check that was started for an older copy (a command or a reload replaced the document meanwhile) says nothing
   * about the current one and is ignored (PS-08).
   */
  private readonly $serverCheck = signal<
    { opened: string; server: string | undefined } | undefined
  >(undefined);
  /**
   * Someone changed the document since it was opened: E4, with a Reload button. Seen either BEFORE saving (the token
   * on the server differs from the one of the opened copy) or when a command is refused with 409 on its If-Match.
   */
  readonly $isStale = computed(() => {
    const check = this.$serverCheck();
    const opened = this.$document()?.concurrencyToken;
    const outdated =
      check !== undefined &&
      check.server !== undefined &&
      check.opened === opened &&
      check.server !== opened;
    return outdated || this.$commandErrors().some(isStaleDocumentError);
  });
  /** A rejected submit / post because stock moved (409 with product, available, requested): E5. */
  readonly $stockConflict = computed(() =>
    this.$postingErrors()
      .map(error => this.toStockConflict(error))
      .find(isDefined)
  );
  /**
   * A reversal the stock cannot take back (E6): a posted document cannot be adjusted, so the advice differs from E5.
   * The API names every product that would go below zero (`shortages`); an older API named one, through the
   * insufficient-stock members, and that case is read too.
   */
  readonly $reverseBlocked = computed(() => {
    const items = this.reversalItems(this.documents.reverse.$operation().error);
    return items.length > 0
      ? { docNo: this.$document()?.no ?? '', items: items.join(', ') }
      : undefined;
  });
  /** Who changed the document and when, when the API says so (E4); the banner falls back to the plain sentence. */
  readonly $staleDetails = computed(() =>
    this.$commandErrors()
      .map(error => staleDetails(error))
      .find(isDefined)
  );
  /** What to tell the user when posting failed on stock: edit (draft), cancel and redo (submitted), or ask (no right). */
  readonly $stockConflictBodyKey = computed<MessageKey>(() => {
    if (this.$editable()) {
      return 'doc.stockChangedBody';
    }
    return this.$actions().has(DOCUMENT_ACTION.Cancel)
      ? 'doc.stockChangedBodyCancel'
      : 'doc.stockChangedBodyAsk';
  });
  /** One line under the title for what the status chip alone does not say (design 7.1). */
  readonly $statusHint = computed<StatusHint | undefined>(() => {
    const document = this.$document();
    switch (document?.status) {
      case DocumentStatus.Submitted:
        return document.requiresApproval ? undefined : { key: 'doc.hint.readyToPost' };
      case DocumentStatus.Approved:
        return {
          key: 'doc.hint.approvedBy',
          name: document.approvedByName ?? document.approvedBy ?? '',
          at: document.approvedAt
        };
      case DocumentStatus.Posted:
        return {
          key: 'doc.hint.postedBy',
          name: document.postedByName ?? document.postedBy ?? '',
          at: document.postedAt
        };
      default:
        return undefined;
    }
  });
  /** NV-05: a worker without the Reverse right has no button; say what to do about a wrong quantity. */
  readonly $askToReverse = computed(
    () =>
      this.$status() === DocumentStatus.Posted &&
      !this.$actions().has(DOCUMENT_ACTION.Reverse) &&
      this.permissions.has(PERMISSION.DocumentCreate)
  );
  /** The action bar is only worth its space when it has something to show. */
  readonly $showBar = computed(
    () => this.$editable() || this.$allowedActions().length > 0 || this.$selfApprovalBlocked()
  );
  readonly $saveGeneralErrors = computed(() => {
    const error = this.documents.save.$operation().error;
    if (!error) {
      return [];
    }
    return Object.entries(error.fieldErrors)
      .filter(([field]) => !this.view.header.get(field) && !field.startsWith('lines'))
      .flatMap(([, messages]) => messages);
  });

  /** The URL caught up with a document this editor created: the route drives from now on. */
  private readonly releaseCreatedId = effect(() => {
    if (this.$routeDocumentId() !== undefined) {
      untracked(() => this.$createdId.set(undefined));
    }
  });

  /** Opens what the route names; also when the same page is reused for another document (EA-20). */
  private readonly openDocument = effect(() => {
    const id = this.$documentId();
    untracked(() => {
      if (id === undefined) {
        this.documents.detail.reset();
        this.view.startNew(null);
        this.prefillFromLink();
      } else if (id === this.justCreatedId) {
        this.justCreatedId = undefined;
      } else {
        this.documents.detail.load(id);
      }
    });
  });

  private readonly syncTitle = effect(() => {
    const detail = this.$isNew()
      ? this.i18n.t(this.$isIssue() ? 'issues.new' : 'receipts.new')
      : this.$document()?.no;
    this.pageTitle.setDetail(detail);
  });

  private readonly populateForm = effect(() => {
    const document = this.$document();
    const editable = this.$editable();
    if (!document) {
      return;
    }
    // The lines already carry `available` (stock on hand now), so opening a document needs no stock query.
    untracked(() => this.view.populate(document, editable));
  });

  private readonly preselectWarehouse = effect(() => {
    const options = this.$warehouseOptions();
    if (
      this.$isNew() &&
      options.length === 1 &&
      this.view.header.controls.warehouseId.value === null
    ) {
      untracked(() => this.view.header.controls.warehouseId.setValue(Number(options[0].value)));
    }
  });

  private readonly loadLedger = effect(() => {
    const documentId = this.$documentId();
    if (this.$isLedgerTab() && documentId !== undefined) {
      untracked(() =>
        this.stock.ledger.load({ sourceDocId: documentId, page: 1, pageSize: LEDGER_PAGE_SIZE })
      );
    }
  });

  private readonly showSaveErrors = effect(() => {
    const error = this.documents.save.$operation().error;
    if (error) {
      untracked(() => applyFieldErrors(this.view.header, error));
    }
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.pageTitle.setDetail(undefined));
  }

  ngOnInit(): void {
    this.view.configure(this.$type());
    this.catalog.ensureLoaded();
    this.warehouses.ensureLoaded();
    this.settings.ensureLoaded();
    this.partners.loadActive(this.$isIssue() ? PartnerType.Customer : PartnerType.Supplier);

    this.view.header.controls.warehouseId.valueChanges
      .pipe(
        filter(isDefined),
        filter(() => this.$isIssue() && this.$editable()),
        // Loading the saved document sets its own warehouse: only a different warehouse needs fresh stock figures.
        filter(warehouseId => this.view.needsAvailability(warehouseId)),
        switchMap(warehouseId => this.view.refreshAvailability(warehouseId)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  hasUnsavedChanges(): boolean {
    return this.$editable() && this.view.hasUnsavedChanges();
  }

  /** Back on this tab after a while: has someone changed the document meanwhile? (one cheap list call) */
  onVisibilityChange(): void {
    const opened = this.$document();
    if (this.page.visibilityState !== 'visible' || !opened || this.$isBusy()) {
      return;
    }
    // Remember WHICH copy was checked: if a command or a reload replaces it before the answer arrives, the answer is moot.
    const checked = opened.concurrencyToken;
    this.documents
      .currentToken(opened)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(server => this.$serverCheck.set({ opened: checked, server }));
  }

  /** A reload or a close of the tab with a half-written document asks first (NV-07). */
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  onTabChange(tab: string): void {
    this.$tab.set(tab);
  }

  onOpenAddItem(): void {
    this.$lastAdded.set(undefined);
    this.$addItemOpen.set(true);
  }

  /** Says whether the line is new or one more of an existing one, with the quantity now (NV-04). */
  onProductPicked(picked: PickedProduct): void {
    const added = this.view.addProduct(picked.product, picked.available);
    if (added === undefined) {
      return;
    }
    this.$lastAdded.set(
      this.i18n.t(added.increased ? 'doc.itemIncreased' : 'doc.itemAdded', {
        sku: picked.product.sku,
        qty: added.qty
      })
    );
  }

  /** The user is searching for something else: the message about the last pick is out of date. */
  onPickerTermChanged(): void {
    this.$lastAdded.set(undefined);
  }

  onStep(event: { group: LineGroup; delta: number }): void {
    this.view.step(event.group, event.delta);
  }

  /** A line with typed work (another quantity, a cost, a remark) asks first: there is no Undo (NV-18). */
  onRemoveLine(index: number): void {
    const line = this.$values()[index];
    const hasTypedWork =
      line !== undefined &&
      (line.qty !== DEFAULT_QTY || line.unitCost !== null || line.remark.trim().length > 0);
    if (!hasTypedWork) {
      this.view.remove(index);
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.removeLine.title', { sku: line.sku }),
        body: this.i18n.t('dialog.removeLine.body'),
        confirmLabel: this.i18n.t('common.remove'),
        cancelLabel: this.i18n.t('dialog.removeLine.keep'),
        destructive: true
      })
      .pipe(
        tap(() => this.view.remove(index)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  onSetToAvailable(group: LineGroup): void {
    this.view.setToAvailable(group);
  }

  onRetryLoad(): void {
    this.documents.detail.reload();
  }

  /** E4: drop the outdated copy (and the edits on it) and read the document again. */
  onReload(): void {
    this.documents.resetCommands();
    this.$serverCheck.set(undefined);
    this.documents.detail.reload();
  }

  onAction(action: DocumentAction): void {
    this.$activeAction.set(action);
    switch (action) {
      case DOCUMENT_ACTION.Edit:
        this.saveDraft();
        break;
      case DOCUMENT_ACTION.Submit:
        this.submit();
        break;
      case DOCUMENT_ACTION.Approve:
        this.approve();
        break;
      case DOCUMENT_ACTION.Reject:
        this.reject();
        break;
      case DOCUMENT_ACTION.Cancel:
        this.cancel();
        break;
      case DOCUMENT_ACTION.Post:
        this.runPost(this.$document())
          .pipe(
            finalize(() => this.settleFocus()),
            takeUntilDestroyed(this.destroyRef)
          )
          .subscribe();
        break;
      case DOCUMENT_ACTION.Reverse:
        this.reverse();
        break;
    }
  }

  /** "DEMO-001 (-3 PCS)" for each product a refused reversal would leave negative. */
  private reversalItems(error: ApiError | undefined): string[] {
    if (isReversalBlockedError(error)) {
      return reversalShortages(error).map(
        shortage => `${shortage.sku} (${shortage.balance} ${shortage.uomCode})`
      );
    }
    // An older API named one product through the insufficient-stock members.
    const legacy = this.toStockConflict(error);
    return legacy ? [`${legacy.sku} (${legacy.available - legacy.requested} ${legacy.uom})`] : [];
  }

  /** From Low stock: the warehouse and the product are chosen already and the cursor lands on the quantity (NV-13). */
  private prefillFromLink(): void {
    const warehouseId = Number(this.$warehouseParam());
    if (Number.isInteger(warehouseId) && warehouseId > 0) {
      this.view.header.controls.warehouseId.setValue(warehouseId);
    }
    const sku = this.$sku();
    if (!sku) {
      return;
    }
    this.products
      .lookup(sku)
      .pipe(
        filter(result => result.found && result.product?.isActive === true),
        tap(result => {
          if (result.product) {
            this.view.addProduct(result.product, null);
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() =>
        afterNextRender(
          () => this.page.querySelector<HTMLElement>('app-document-line-editor input')?.focus(),
          { injector: this.injector }
        )
      );
  }

  /** The last chance to catch a wrong quantity: say what is being posted, not just how many lines (NV-19). */
  private postSummary(document: DocumentDto): string {
    const items = document.lines
      .slice(0, POST_SUMMARY_LINES)
      .map(line => `${line.sku} x ${line.qty} ${line.uomCode}`)
      .join(', ');
    const more = document.lines.length - POST_SUMMARY_LINES;
    return more > 0
      ? this.i18n.t('dialog.post.summaryMore', { items, count: more })
      : this.i18n.t('dialog.post.summary', { items });
  }

  /** Where each short line ends up, so the approver sees the cost of the override ("QA-004 will go to -40"). */
  private overrideBalances(document: DocumentDto): string {
    return document.lines
      .filter(line => line.available !== undefined && line.available !== null)
      .map(line => ({ line, balance: (line.available ?? 0) - line.baseQty }))
      .filter(({ balance }) => balance < 0)
      .map(({ line, balance }) =>
        this.i18n.t('dialog.approveOverride.line', {
          sku: line.sku,
          balance,
          uom: line.uomCode
        })
      )
      .join(' ');
  }

  /** The insufficient-stock 409 (product, available, requested), or undefined for any other error. */
  private toStockConflict(error: ApiError | undefined): StockConflict | undefined {
    if (!isStockConflictError(error) || !error) {
      return undefined;
    }
    const productId = Number(error.extensions['productId']);
    const line = this.$values().find(value => value.productId === productId);
    return {
      sku: line?.sku ?? String(productId),
      available: Number(error.extensions['available'] ?? 0),
      requested: Number(error.extensions['requested'] ?? 0),
      uom: line?.baseUomCode ?? ''
    };
  }

  private saveDraft(): void {
    if (!this.validate()) {
      return;
    }
    this.save()
      .pipe(
        tap(() => this.toast.success(this.i18n.t('doc.toast.draftSaved'))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private submit(): void {
    if (!this.validate()) {
      return;
    }
    this.save()
      .pipe(
        switchMap(document => this.documents.submit.run(toDocumentRef(document))),
        switchMap(document => this.afterSubmit(document)),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private approve(): void {
    const document = this.$document();
    if (!document) {
      return;
    }
    // A stock override is approved WITH its reason (the API refuses an empty one); posting takes no reason.
    const override = document.requiresOverride;
    this.confirm
      .ask({
        heading: this.i18n.t(override ? 'dialog.approveOverride.title' : 'dialog.approve.title', {
          docNo: document.no
        }),
        body: override
          ? `${this.i18n.t('dialog.approveOverride.body')} ${this.overrideBalances(document)}`.trim()
          : this.i18n.t('dialog.approve.body'),
        confirmLabel: this.i18n.t('doc.action.approve'),
        cancelLabel: this.i18n.t('common.back'),
        ...(override && {
          reasonLabel: this.i18n.t('dialog.approveOverride.reason'),
          reasonRequired: true,
          minReasonLength: MIN_REASON_LENGTH
        })
      })
      .pipe(
        switchMap(reason =>
          this.documents.approve.run({
            ...toDocumentRef(document),
            negativeStockOverrideReason: override ? reason : undefined
          })
        ),
        tap(() => this.toast.success(this.i18n.t('doc.toast.approved'))),
        tap(() => this.afterChange(false)),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private reject(): void {
    const document = this.$document();
    if (!document) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.reject.title', { docNo: document.no }),
        body: this.i18n.t('dialog.reject.body', {
          name: document.createdByName || document.createdBy
        }),
        confirmLabel: this.i18n.t('doc.action.reject'),
        cancelLabel: this.i18n.t('common.back'),
        destructive: true,
        reasonLabel: this.i18n.t('dialog.reason'),
        reasonRequired: true,
        minReasonLength: MIN_REASON_LENGTH
      })
      .pipe(
        switchMap(reason => this.documents.reject.run({ ...toDocumentRef(document), reason })),
        tap(() =>
          this.toast.success(
            this.i18n.t('doc.toast.returned', {
              name: document.createdByName || document.createdBy
            })
          )
        ),
        tap(() => this.afterChange(false)),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private cancel(): void {
    const document = this.$document();
    if (!document) {
      void this.router.navigate(this.$backLink());
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.cancel.title', { docNo: document.no }),
        body: this.i18n.t('dialog.cancel.body'),
        confirmLabel: this.i18n.t('doc.action.cancel'),
        cancelLabel: this.i18n.t('dialog.cancel.keep'),
        destructive: true,
        reasonLabel: this.i18n.t('dialog.reasonOptional')
      })
      .pipe(
        switchMap(reason => this.documents.cancel.run({ ...toDocumentRef(document), reason })),
        tap(() => this.toast.success(this.i18n.t('doc.toast.cancelled'))),
        tap(() => this.afterChange(false)),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private reverse(): void {
    const document = this.$document();
    if (!document) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.reverse.title', { docNo: document.no }),
        body: this.i18n.t('dialog.reverse.body'),
        confirmLabel: this.i18n.t('doc.action.reverse'),
        cancelLabel: this.i18n.t('common.back'),
        destructive: true,
        reasonLabel: this.i18n.t('dialog.reason'),
        reasonRequired: true,
        minReasonLength: MIN_REASON_LENGTH
      })
      .pipe(
        switchMap(reason => this.documents.reverse.run({ ...toDocumentRef(document), reason })),
        tap(() => this.toast.success(this.i18n.t('doc.toast.reversed'))),
        tap(() => this.afterChange(true)),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Confirm, then post. Posting takes no reason: a stock override was explained when the document was approved. */
  private runPost(document: DocumentDto | undefined): Observable<DocumentDto> {
    if (!document) {
      return of();
    }
    const direction = document.type === DocumentType.Issue ? 'decrease' : 'increase';
    return this.confirm
      .ask({
        heading: this.i18n.t('dialog.post.title', { docNo: document.no }),
        body: `${this.i18n.t(
          direction === 'decrease' ? 'dialog.post.bodyDecrease' : 'dialog.post.bodyIncrease',
          { count: document.lines.length }
        )} ${this.postSummary(document)}`,
        confirmLabel: this.i18n.t('doc.action.post'),
        cancelLabel: this.i18n.t('common.back')
      })
      .pipe(
        switchMap(() => this.documents.post.run(toDocumentRef(document))),
        tap(() => this.toast.success(this.i18n.t('doc.toast.posted'))),
        tap(() => this.afterChange(true))
      );
  }

  private afterSubmit(document: DocumentDto): Observable<DocumentDto> {
    if (document.requiresApproval) {
      this.toast.success(this.i18n.t('doc.toast.sentForApproval'));
      this.afterChange(false);
      return of(document);
    }
    if (toDocumentActions(document.allowedActions).has(DOCUMENT_ACTION.Post)) {
      return this.runPost(document);
    }
    this.toast.success(this.i18n.t('doc.toast.submitted'));
    this.afterChange(false);
    return of(document);
  }

  private save(): Observable<DocumentDto> {
    const opened = this.$document();
    return this.documents.save
      .run({
        type: this.$type(),
        ref: opened ? toDocumentRef(opened) : undefined,
        body: this.view.toRequest()
      })
      .pipe(tap(document => this.bindToCreated(document)));
  }

  private validate(): boolean {
    this.view.markAllTouched();
    this.$showLineError.set(this.$noLines());
    const valid = this.view.isValid();
    if (!valid) {
      this.focusFirstInvalid();
    }
    return valid;
  }

  /** After a failed submit the focus goes to the first field with an error, or to Add item when there is no line (EA-06). */
  private focusFirstInvalid(): void {
    afterNextRender(
      () => {
        const invalid = this.page.querySelector<HTMLElement>(
          'input[aria-invalid="true"], select[aria-invalid="true"], textarea[aria-invalid="true"], fieldset[aria-invalid="true"] input'
        );
        const target =
          invalid ?? this.page.querySelector<HTMLElement>('.document-editor-lines-header button');
        target?.focus();
      },
      { injector: this.injector }
    );
  }

  /**
   * The moment a new document exists on the server the editor becomes ITS editor and the URL follows (NV-01, NV-02):
   * the form is clean (it mirrors the server), so the move does not ask "Discard changes?", and a second Save or Submit
   * - also after Back in the Post dialog - updates this document instead of creating another one.
   */
  private bindToCreated(document: DocumentDto): void {
    if (!this.$isNew()) {
      return;
    }
    this.justCreatedId = document.id;
    this.$createdId.set(document.id);
    this.view.markPristine();
    void this.router.navigate(['/', DOCUMENT_ROUTE[this.$type()], document.id], {
      replaceUrl: true
    });
  }

  /**
   * A command closes its dialog and then disables or removes the button that opened it, which leaves the focus on
   * <body> and a keyboard user back at the top of the page (EA-07). Move it to the error banner, else to the title.
   */
  private settleFocus(): void {
    afterNextRender(
      () => {
        const active = this.page.activeElement;
        if (active && active !== this.page.body) {
          return;
        }
        // The error first (it comes after the title in the page, so one combined selector would pick the title).
        const target =
          this.page.querySelector<HTMLElement>('app-banner[role="alert"][tabindex]') ??
          this.page.querySelector<HTMLElement>('app-page-header h1');
        target?.focus();
      },
      { injector: this.injector }
    );
  }

  /** Keeps menu badges and the dashboard honest after a state change. */
  private afterChange(stockChanged: boolean): void {
    if (this.permissions.has(PERMISSION.StockRead)) {
      this.documents.counts.load();
    }
    if (stockChanged) {
      this.stock.summary.load();
    }
  }
}
