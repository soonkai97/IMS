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
  ApprovalMetric,
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
  ROLE_LABELS,
  MessageKey,
  TranslatePipe
} from '@ims/core/i18n';
import {
  AuthService,
  CanDirective,
  PermissionService,
  SessionDraftService
} from '@ims/core/identity';
import { HasUnsavedChanges, PageTitleService } from '@ims/core/navigation';
import { handledInline } from '@ims/core/http';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { SystemSettingsStore } from '@ims/core/settings';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import {
  DOCUMENT_ROUTE,
  DEFAULT_QTY,
  DocumentsStore,
  LineGroup,
  LineValue,
  NEW_DOCUMENT_ID,
  STATUS_TONE,
  isOverReceiptError,
  isReversalBlockedError,
  isStaleDocumentError,
  isStockConflictError,
  overReceiptLines,
  overRemainingLines,
  reversalShortages,
  staleDetails,
  toDocumentRef
} from '@ims/features/documents';
import { PURCHASE_ORDER_ROUTE, PurchaseOrdersStore } from '@ims/features/purchasing';
import { StockStore } from '@ims/features/stock';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  DOCUMENT_ACTION,
  DocumentAction,
  HTTP_STATUS,
  MIN_REASON_LENGTH,
  PERMISSION,
  PublicId,
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
  UiFormLegendComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPublishHeightDirective,
  UiRadioGroupComponent,
  UiSelectComponent,
  UiSkeletonComponent,
  UiStatusChipComponent,
  UiTableComponent,
  UiTabPanelDirective,
  UiTabsComponent,
  UiTextareaComponent
} from '@ims/ui';
import { applyFieldErrors, controlSignal, isDefined, normalizePublicId } from '@ims/utils';
import { DocumentActionsComponent } from '../document-actions/document-actions.component';
import { DocumentLineEditorComponent } from '../line-editor/line-editor.component';
import { parseEditorDraft } from './document-editor.draft';
import {
  LineRefusal,
  lineRefusals,
  orderLineContexts,
  orderProgress
} from './document-editor.linked-order';
import { DocumentEditorViewService } from './document-editor.view-service';

const TAB_ITEMS = 'items';
const TAB_HISTORY = 'history';
const TAB_LEDGER = 'ledger';
const LEDGER_PAGE_SIZE = 100;
/** How many lines the Post dialog names; the rest is "and N more". */
const POST_SUMMARY_LINES = 3;

/** What posting does to stock, for the confirm dialogs: an issue takes stock out, every other type puts it in. */
function stockDirectionKey(type: DocumentType): MessageKey {
  return type === DocumentType.Issue ? 'dialog.post.bodyDecrease' : 'dialog.post.bodyIncrease';
}

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
    CanDirective,
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
    UiFormLegendComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPublishHeightDirective,
    UiRadioGroupComponent,
    UiSelectComponent,
    UiSkeletonComponent,
    UiStatusChipComponent,
    UiTableComponent,
    UiTabPanelDirective,
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
  private readonly auth = inject(AuthService);
  private readonly catalog = inject(CatalogStore);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly documents = inject(DocumentsStore);
  private readonly drafts = inject(SessionDraftService);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private readonly orders = inject(PurchaseOrdersStore);
  private readonly pageTitle = inject(PageTitleService);
  private readonly partners = inject(PartnersStore);
  private readonly permissions = inject(PermissionService);
  private readonly products = inject(ProductsStore);
  /** The same formatting as the templates (D1): money has 2 decimals, a quantity keeps its own. */
  private readonly moneyPipe = new AppMoneyPipe();
  private readonly qtyPipe = new AppQtyPipe();
  private readonly router = inject(Router);
  private readonly settings = inject(SystemSettingsStore);
  private readonly stock = inject(StockStore);
  private readonly toast = inject(ToastService);
  private readonly warehouses = inject(WarehousesStore);
  readonly view = inject(DocumentEditorViewService);
  /** The shortage banner offers Cancel document as a button of its own. */
  readonly cancelAction = DOCUMENT_ACTION.Cancel;
  readonly orderRoute = PURCHASE_ORDER_ROUTE;
  /** The over-receipt alert offers Edit items, which is the recall. */
  readonly recallAction = DOCUMENT_ACTION.Recall;

  /** Route data. */
  $type = input.required<DocumentType>({ alias: 'type' });
  /** Route parameter; absent on /new. */
  $id = input<string | undefined>(undefined, { alias: 'id' });
  /** Query parameters of a link that starts a document for a product (Low stock: Create receipt). */
  $sku = input<string | undefined>(undefined, { alias: 'sku' });
  $warehouseParam = input<string | undefined>(undefined, { alias: 'warehouseId' });

  /** The button the last action came from: a dismissed dialog hands the focus back to it. */
  private opener: HTMLElement | null = null;

  /** Id of the document this editor just created: the store already holds it, so the route change needs no reload. */
  private justCreatedId: PublicId | undefined;

  $tab = signal<string>(TAB_ITEMS);
  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  $showLineError = signal(false);
  /** Said once after sign-in when unsent changes from before a session expiry were put back, or could not be. */
  $draftNotice = signal<MessageKey | undefined>(undefined);

  /** The id in the URL; undefined on `/new` and when it is not a GUID. */
  private readonly $routeDocumentId = computed(() => {
    const id = this.$id();
    return id === undefined || id === NEW_DOCUMENT_ID ? undefined : normalizePublicId(id);
  });
  /** An old bookmark (`/receipts/123`): not a document, so it is a not-found page and no request is made. */
  private readonly $unusableRouteId = computed(() => {
    const id = this.$id();
    return id !== undefined && id !== NEW_DOCUMENT_ID && this.$routeDocumentId() === undefined;
  });
  /**
   * Set the moment a new document is created. The editor is bound to the saved document from then on (so a repeated
   * Save or Submit updates it instead of creating a second one) even before the router has moved `/new` to `/<id>`.
   */
  private readonly $createdId = signal<PublicId | undefined>(undefined);
  readonly $documentId = computed(() => this.$routeDocumentId() ?? this.$createdId());
  readonly $isNew = computed(() => this.$documentId() === undefined && !this.$unusableRouteId());
  readonly $isIssue = computed(() => this.$type() === DocumentType.Issue);
  readonly $document = computed<DocumentDto | undefined>(() => {
    const document = this.documents.detail.$data();
    return document && document.id === this.$documentId() ? document : undefined;
  });
  /** The purchase order this receipt belongs to. The API leaves it out (null) for an ordinary receipt and for a caller who may not read purchase orders. Supplier, warehouse and lines are the order's: they cannot be changed or added to here. */
  readonly $linkedOrder = computed(() => {
    const order = this.$document()?.purchaseOrder;
    return isDefined(order) ? order : undefined;
  });
  /** Only the id: a save brings a new document object, and the order is not read again for that. */
  private readonly $linkedOrderId = computed(() => this.$linkedOrder()?.id);
  /** The order's own figures per line, once they have arrived (the API stays the authority; they only make the advice readable). */
  readonly $orderLines = computed(() =>
    orderLineContexts(this.orders.detail.$data(), this.$linkedOrderId())
  );
  /** A quantity above what the order has left: advisory here, refused by the API at save and at posting. */
  readonly $overOrder = computed(
    () =>
      this.$linkedOrder() !== undefined &&
      this.$editable() &&
      overRemainingLines(this.$values(), this.$orderLines()).size > 0
  );
  /** What posting this receipt did to the order (closed, closed short, or how many lines are left). Nothing while the order is being read again: after a post it still holds the figures from before. */
  readonly $orderProgress = computed(() => {
    const order = this.orders.detail.$data();
    return order !== undefined &&
      !this.orders.detail.$isLoading() &&
      this.$status() === DocumentStatus.Posted &&
      order.id === this.$linkedOrderId()
      ? orderProgress(order)
      : undefined;
  });
  /** A receipt whose lines may carry an order's prices (cost data): one the API says is linked, and a saved receipt of a caller who sees prices but may not read orders (the API hides the link from them). Nothing of it is kept on the device. */
  private readonly $holdsOrderPrices = computed(
    () =>
      this.$linkedOrder() !== undefined ||
      (!this.$isNew() &&
        !this.$isIssue() &&
        this.permissions.has(PERMISSION.CostView) &&
        !this.permissions.has(PERMISSION.PoRead))
  );
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
  /** The load call raises no toast; this banner is the one announcement, with the sentence the interceptor mapped. */
  readonly $loadErrorMessage = computed(() => this.documents.detail.$operation().error?.message);
  /** The id in the URL belongs to no document (an old bookmark): a page of its own, and retrying cannot help. */
  readonly $notFound = computed(
    () =>
      this.$unusableRouteId() ||
      this.documents.detail.$operation().error?.kind === API_ERROR_KIND.NotFound
  );
  readonly $canPost = computed(() => this.permissions.has(PERMISSION.DocumentPost));
  /** The order opens for a holder of po.read (the API shows the link to nobody else). */
  readonly $canReadOrders = computed(() => this.permissions.has(PERMISSION.PoRead));
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
      this.documents.recall.$isRunning() ||
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
      this.permissions.hasAny([PERMISSION.DocumentApprove, PERMISSION.DocumentApproveLarge]) &&
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
    {
      key: 'sku',
      type: 'code',
      header: this.i18n.t('product.sku'),
      value: row => row.sku,
      primary: true
    },
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
    this.documents.recall.$operation().error,
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
  /** Posting refused because the order has less left than this receipt brings (409 over_receipt): the figures of each line, in one alert. */
  readonly $overReceipt = computed(() => {
    const error = this.$postingErrors().find(isOverReceiptError);
    return error
      ? { lines: overReceiptLines(error), message: error.message ?? error.detail }
      : undefined;
  });
  /** Any other 409 of a command that is not a stale copy, stock or a blocked reversal (the order of a receipt is no longer open, the module is off): the API's own sentence in one alert. The interceptor does not toast a 409 on an If-Match call. */
  readonly $conflictRefusal = computed(
    () =>
      this.$commandErrors().find(
        error =>
          error?.kind === API_ERROR_KIND.Conflict &&
          !isStaleDocumentError(error) &&
          !isStockConflictError(error) &&
          !isOverReceiptError(error) &&
          !isReversalBlockedError(error)
      )?.message
  );
  /** The 400 sentences about lines (a quantity above what remains), each with the SKU it was about when it came: the error counts lines by position, so the list is dropped as soon as a line is changed or removed. */
  readonly $lineRefusals = signal<readonly LineRefusal[]>([]);
  /** Edit items (take it back to Draft) is offered under the over-receipt alert to whoever may. */
  readonly $canRecall = computed(() => this.$actions().has(DOCUMENT_ACTION.Recall));
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
  /**
   * What to tell the user when posting failed on stock: adjust (draft), tap Edit (the submitter), wait or cancel (a
   * reviewer of an approved document), cancel and redo (submitted), or ask (no right).
   */
  readonly $stockConflictBodyKey = computed<MessageKey>(() => {
    if (this.$editable()) {
      return 'doc.stockChangedBody';
    }
    if (this.$actions().has(DOCUMENT_ACTION.Recall)) {
      return 'doc.stockChangedBodyEdit';
    }
    if (this.$actions().has(DOCUMENT_ACTION.Cancel)) {
      return this.$status() === DocumentStatus.Approved
        ? 'doc.stockChangedBodyReviewer'
        : 'doc.stockChangedBodyCancel';
    }
    return 'doc.stockChangedBodyAsk';
  });
  /**
   * The buttons of the bar. While the "Stock changed" banner is on screen Post is not offered (NR-11): the page says the
   * document cannot be posted, so a Post button next to it would contradict it. The server still decides at the next load.
   */
  readonly $barActions = computed<readonly string[]>(() =>
    this.$stockConflict() === undefined
      ? this.$allowedActions()
      : this.$allowedActions().filter(action => action !== DOCUMENT_ACTION.Post)
  );
  /** An approved issue the reviewer can see is short on stock (PR-02): says so before anyone presses Post. */
  readonly $cannotPost = computed(() => {
    const document = this.$document();
    if (
      !document ||
      document.type !== DocumentType.Issue ||
      document.status !== DocumentStatus.Approved ||
      document.negativeStockOverrideReason ||
      !this.$actions().has(DOCUMENT_ACTION.Cancel) ||
      this.$stockConflict() !== undefined
    ) {
      return undefined;
    }
    // Lines of one product add up, as the API does; `available` is the same figure on each of them.
    const needs = new Map<string, { sku: string; requested: number; available: number }>();
    for (const line of document.lines) {
      if (line.available === undefined || line.available === null) {
        continue;
      }
      const need = needs.get(line.productId) ?? {
        sku: line.sku,
        requested: 0,
        available: line.available
      };
      needs.set(line.productId, { ...need, requested: need.requested + line.baseQty });
    }
    const short = [...needs.values()].find(need => need.requested > need.available);
    return short ? { ...short, warehouse: document.warehouseName } : undefined;
  });
  /** One line under the title for what the status chip alone does not say (design 7.1). */
  readonly $statusHint = computed<StatusHint | undefined>(() => {
    const document = this.$document();
    switch (document?.status) {
      case DocumentStatus.Submitted:
        // A running command says what it is doing on its button; a hint saying "Ready to post" beside "Posting..." contradicts it.
        if (document.requiresApproval || this.$stockConflict() !== undefined || this.$isBusy()) {
          return undefined;
        }
        // "Ready to post" is for someone who can post it: a clerk is told whom the document waits for.
        return this.$actions().has(DOCUMENT_ACTION.Post)
          ? { key: 'doc.hint.readyToPost' }
          : { key: 'doc.hint.waitingToPost' };
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
  /** Why an approval rule held the document back, in words (PS-03): the value or quantity that reached a limit. */
  readonly $approvalTriggers = computed(() => {
    return (this.$document()?.approvalTriggers ?? []).map(trigger => {
      const isAmount = trigger.metric === ApprovalMetric.Amount;
      const format = isAmount ? this.moneyPipe : this.qtyPipe;
      // A caller without cost.view gets the kind of rule and the approver, but null numbers for an amount rule.
      const known = isDefined(trigger.actual) && isDefined(trigger.threshold);
      return {
        key: this.triggerKey(isAmount, known),
        actual: known ? format.transform(trigger.actual ?? 0) : '',
        threshold: known ? format.transform(trigger.threshold ?? 0) : '',
        role: this.i18n.enumLabel(ROLE_LABELS, trigger.roleRequired)
      };
    });
  });
  /** NV-05: a worker without the Reverse right has no button; say what to do about a wrong quantity. */
  readonly $askToReverse = computed(
    () =>
      this.$status() === DocumentStatus.Posted &&
      !this.$actions().has(DOCUMENT_ACTION.Reverse) &&
      this.permissions.has(PERMISSION.DocumentCreate)
  );
  /** The submitter can take a Submitted document back to Draft: one line that says so, where the Edit button is. */
  readonly $recallHintKey = computed<MessageKey | undefined>(() => {
    if (
      this.$status() !== DocumentStatus.Submitted ||
      !this.$actions().has(DOCUMENT_ACTION.Recall) ||
      this.$isBusy()
    ) {
      return undefined;
    }
    if (this.$document()?.requiresApproval) {
      return 'doc.hint.recallWaiting';
    }
    // The stock banner already says "Tap Edit".
    return this.$stockConflict() === undefined ? 'doc.hint.recallReady' : undefined;
  });
  /** An approved document is not the creator's to take back: say whom to ask (the approver cancels it). */
  readonly $askToCancel = computed(() => {
    const document = this.$document();
    return document !== undefined &&
      document.status === DocumentStatus.Approved &&
      !this.$actions().has(DOCUMENT_ACTION.Cancel) &&
      this.permissions.has(PERMISSION.DocumentCreate)
      ? (document.approvedByName ?? document.approvedBy ?? '')
      : undefined;
  });
  /** The action bar is only worth its space when it has something to show. */
  readonly $showBar = computed(
    () => this.$editable() || this.$barActions().length > 0 || this.$selfApprovalBlocked()
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
    const unusable = this.$unusableRouteId();
    untracked(() => {
      // The store outlives this page (and a sign-out): what the last document's commands failed with is not this one's. The
      // document this editor just created is the same one, so its commands keep their outcome.
      if (id === undefined || id !== this.justCreatedId) {
        this.documents.resetCommands();
      }
      if (unusable) {
        this.documents.detail.reset();
      } else if (id === undefined) {
        this.documents.detail.reset();
        this.view.startNew(null);
        this.prefillFromLink();
        this.restoreDraft(undefined);
      } else if (id === this.justCreatedId) {
        this.justCreatedId = undefined;
      } else {
        // The store outlives this page and may still hold an older copy of this very document: showing it first would fill the
        // form (and take a kept draft) and the answer would then overwrite it. Start from nothing so the form is filled once.
        this.documents.detail.reset();
        this.documents.detail.load(id, handledInline());
      }
    });
  });

  /** A receipt from a purchase order shows the order's figures. The store outlives this page and a sign-out and may hold an older copy of this very order, so it is read again every time a linked receipt opens. */
  private readonly loadLinkedOrder = effect(() => {
    const orderId = this.$linkedOrderId();
    if (orderId !== undefined) {
      untracked(() => {
        this.orders.detail.reset();
        this.orders.detail.load(orderId, handledInline());
      });
    }
  });

  private readonly captureLineRefusals = effect(() => {
    const errors = [
      this.documents.save.$operation().error,
      this.documents.submit.$operation().error
    ];
    untracked(() => {
      const refusals = lineRefusals(errors, this.$values());
      this.$lineRefusals.set(refusals);
      // A refused line says what the order had left when it was asked: read its figures again.
      if (refusals.length > 0 && this.$linkedOrder() !== undefined) {
        this.orders.detail.reload();
      }
    });
  });

  private readonly dropLineRefusals = effect(() => {
    this.$values();
    untracked(() => this.$lineRefusals.set([]));
  });

  /** A refused post is the answer to what the person just did: the alert takes the focus (the closed dialog leaves it on Post). */
  private readonly focusOverReceipt = effect(() => {
    if (this.$overReceipt() !== undefined) {
      untracked(() =>
        afterNextRender(
          () => this.page.querySelector<HTMLElement>('.document-editor-over-receipt')?.focus(),
          { injector: this.injector }
        )
      );
    }
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
    untracked(() => {
      this.view.populate(document, editable);
      this.restoreDraft({ token: document.concurrencyToken, editable });
    });
  });

  private readonly preselectWarehouse = effect(() => {
    const warehouses = this.warehouses.$activeWarehouses();
    if (
      this.$isNew() &&
      warehouses.length === 1 &&
      this.view.header.controls.warehouseId.value === null
    ) {
      untracked(() => this.view.header.controls.warehouseId.setValue(warehouses[0].id));
    }
  });

  private supportLoaded = false;

  private readonly loadSupportWhenOpened = effect(() => {
    if (this.$document()) {
      untracked(() => this.loadSupport());
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

  /**
   * Edit or Cancel refused with a plain 400 (the document is not in a state that allows it any more): the sentence was
   * announced once by the toast, and the page is read again so its buttons match the document. A stale 409 has its banner
   * and Reload button instead (E4); a 400 about a field (the reason) is shown at the field.
   */
  private readonly reloadAfterWrongState = effect(() => {
    const refused = [this.documents.recall, this.documents.cancel].filter(
      command =>
        command.$operation().error?.status === HTTP_STATUS.BadRequest &&
        Object.keys(command.$operation().error?.fieldErrors ?? {}).length === 0
    );
    if (refused.length > 0) {
      untracked(() => {
        // Forget the refusal first, or any later command would run this again for the old error and race its own answer.
        refused.forEach(command => command.reset());
        this.documents.detail.reload();
      });
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
    // The session expired while the form held unsent changes: keep them so the user can sign in and carry on (QA defect 1).
    this.auth.sessionExpiring$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.keepDraft());
  }

  /** Where this editor keeps unsent changes: one entry per document type and document (or the new one). */
  private draftKey(): string {
    return `document.${this.$type()}.${this.$documentId() ?? NEW_DOCUMENT_ID}`;
  }

  private keepDraft(): void {
    // A receipt from an order carries the order's prices, which are cost data: nothing of it is kept on the device.
    if (this.hasUnsavedChanges() && !this.$holdsOrderPrices()) {
      this.drafts.save(
        this.draftKey(),
        this.view.snapshot(this.$document()?.concurrencyToken ?? null)
      );
    }
  }

  /**
   * Puts back the changes kept at a session expiry, once. A saved document takes them only if it is the version they were made
   * on and still editable (the server decides what a Save does with anything else); otherwise they are dropped and the notice
   * says so.
   */
  private restoreDraft(opened: { token: string; editable: boolean } | undefined): void {
    const stored = this.drafts.take<unknown>(this.draftKey());
    // Nothing of a receipt from an order is put back (and nothing was kept): what is found there is not this editor's.
    if (stored === undefined || this.$holdsOrderPrices()) {
      return;
    }
    // What storage holds is untrusted: a draft that fails the check is dropped like one made on another version.
    const draft = parseEditorDraft(stored);
    const usable =
      draft !== undefined &&
      (opened === undefined
        ? draft.basedOn === null
        : opened.editable && draft.basedOn === opened.token);
    if (usable && draft) {
      this.view.restore(draft);
    }
    this.$draftNotice.set(usable ? 'doc.draftRestored' : 'doc.draftOutdated');
  }

  /**
   * Categories, warehouses, settings and partners for the form. A saved document asks for them once it has opened: with the
   * API down, its one banner covers the failure and these calls would only add a toast saying the same thing.
   */
  private loadSupport(): void {
    if (this.supportLoaded) {
      return;
    }
    this.supportLoaded = true;
    this.catalog.ensureLoaded();
    this.warehouses.ensureLoaded();
    this.settings.ensureLoaded();
    this.partners.loadActive(this.$isIssue() ? PartnerType.Customer : PartnerType.Supplier);
  }

  ngOnInit(): void {
    this.view.configure(this.$type());
    if (this.$isNew()) {
      this.loadSupport();
    }

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
        // Only a receipt line has a cost, and warehouse staff may not see it: the word stays out otherwise (NR-09).
        body: this.i18n.t(
          this.$type() === DocumentType.Receipt && this.permissions.has(PERMISSION.CostView)
            ? 'dialog.removeLine.bodyWithCost'
            : 'dialog.removeLine.body'
        ),
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
    this.opener = this.page.activeElement instanceof HTMLElement ? this.page.activeElement : null;
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
      case DOCUMENT_ACTION.Recall:
        this.recall();
        break;
      case DOCUMENT_ACTION.Post:
        this.confirmAndPost(this.$document())
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
  private triggerKey(isAmount: boolean, known: boolean): MessageKey {
    if (!isAmount) {
      return 'doc.trigger.Quantity';
    }
    return known ? 'doc.trigger.Amount' : 'doc.trigger.AmountHidden';
  }

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
    const warehouseId = normalizePublicId(this.$warehouseParam());
    if (warehouseId) {
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
    return this.summaryOf(document.lines.map(line => `${line.sku} x ${line.qty} ${line.uomCode}`));
  }

  /** "Includes A x 1 PCS, B x 2 PCS and 3 more." for lines already written as text. */
  private summaryOf(lines: readonly string[]): string {
    const items = lines.slice(0, POST_SUMMARY_LINES).join(', ');
    const more = lines.length - POST_SUMMARY_LINES;
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
    const productId = error.extensions['productId'];
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
    // One question, asked before anything is sent: after Submit & post the sequence runs to its end without another dialog.
    this.confirmSubmitAndPost()
      .pipe(
        switchMap(() => this.save()),
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
    // Cancelling an approved document undoes someone's decision: the reason is required there (the API refuses without).
    const approved = document.status === DocumentStatus.Approved;
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.cancel.title', { docNo: document.no }),
        body: this.i18n.t(approved ? 'dialog.cancelApproved.body' : 'dialog.cancel.body'),
        confirmLabel: this.i18n.t('doc.action.cancel'),
        cancelLabel: this.i18n.t('dialog.cancel.keep'),
        destructive: true,
        reasonLabel: this.i18n.t(approved ? 'dialog.reason' : 'dialog.reasonOptional'),
        ...(approved && { reasonRequired: true, minReasonLength: MIN_REASON_LENGTH })
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

  /**
   * The submitter takes a Submitted document back to Draft (same number, same lines). The page changes shape (buttons, fields),
   * so the person is always asked first; a document in an approver's list also says it leaves that queue.
   */
  private recall(): void {
    const document = this.$document();
    if (!document) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.recall.title', { docNo: document.no }),
        body: this.i18n.t(
          document.requiresApproval ? 'dialog.recall.body' : 'dialog.recall.bodyReady'
        ),
        confirmLabel: this.i18n.t('dialog.recall.confirm'),
        cancelLabel: this.i18n.t(
          document.requiresApproval ? 'dialog.recall.keep' : 'dialog.recall.keepReady'
        )
      })
      .pipe(
        switchMap(() => this.runRecall(document)),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** The answer is the Draft with its new version: the store keeps it as the opened document, so the form opens on it. */
  private runRecall(document: DocumentDto): Observable<DocumentDto> {
    return this.documents.recall.run(toDocumentRef(document)).pipe(
      tap(() => this.toast.success(this.i18n.t('doc.toast.recalled'))),
      tap(() => this.afterChange(false))
    );
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
  private confirmAndPost(document: DocumentDto | undefined): Observable<DocumentDto> {
    if (!document) {
      return of();
    }
    // The last chance to catch a wrong quantity: whoever may take the document back gets "Edit items" instead of "Back".
    const mayRecall = toDocumentActions(document.allowedActions).has(DOCUMENT_ACTION.Recall);
    return this.confirm
      .choose({
        heading: this.i18n.t('dialog.post.title', { docNo: document.no }),
        body: `${this.i18n.t(stockDirectionKey(document.type), {
          count: document.lines.length
        })} ${this.postSummary(document)}`,
        confirmLabel: this.i18n.t('doc.action.post'),
        cancelLabel: this.i18n.t('common.back'),
        ...(mayRecall && { alternateLabel: this.i18n.t('dialog.post.edit') })
      })
      .pipe(
        switchMap(answer =>
          answer.choice === 'alternate' ? this.runRecall(document) : this.postDocument(document)
        )
      );
  }

  /** The post itself: its button shows "Posting..." until the answer, then the page is the Posted document. */
  private postDocument(document: DocumentDto, afterSubmit = false): Observable<DocumentDto> {
    this.$activeAction.set(DOCUMENT_ACTION.Post);
    return this.documents.post
      .run({ ...toDocumentRef(document), ...(afterSubmit && { reportInline: true }) })
      .pipe(
        tap(() => this.toast.success(this.i18n.t('doc.toast.posted'))),
        tap(() => this.afterChange(true)),
        finalize(() => {
          // Refused for more than the order has left: its figures may have moved since this page opened.
          if (this.$overReceipt() !== undefined) {
            this.orders.detail.reload();
          }
          // The submit worked and the post did not: one sentence says so, with the reason, and the document stays Submitted.
          const error = this.documents.post.$operation().error;
          if (afterSubmit && error) {
            this.afterChange(false);
            // A banner already says why (stock changed, someone else changed the document), and a lost session sends the person
            // to sign in: a toast on top of either would be a second announcement.
            const bannerShown =
              this.$stockConflict() !== undefined ||
              this.$isStale() ||
              this.$overReceipt() !== undefined ||
              this.$conflictRefusal() !== undefined;
            if (!bannerShown && error.kind !== API_ERROR_KIND.Unauthorized) {
              this.toast.error(
                this.i18n.t('doc.toast.notPosted', { reason: error.message ?? error.detail })
              );
            }
          }
        })
      );
  }

  /**
   * Submit & post is one question, asked up front: whoever may post is told what it does (and that an approval rule can stop
   * it at Submitted) before anything is sent. Whoever cannot post just submits.
   */
  private confirmSubmitAndPost(): Observable<unknown> {
    if (!this.$canPost()) {
      return of(true);
    }
    const items = this.$values().map(
      value => `${value.sku} x ${value.qty ?? 0} ${this.unitCode(value)}`
    );
    return this.confirm.ask({
      heading: this.i18n.t('dialog.submitPost.title'),
      body: [
        this.i18n.t(stockDirectionKey(this.$type()), {
          count: items.length
        }),
        this.summaryOf(items),
        this.i18n.t('dialog.submitPost.approval')
      ].join('\n'),
      confirmLabel: this.i18n.t('doc.action.submitAndPost'),
      cancelLabel: this.i18n.t('common.back')
    });
  }

  private unitCode(value: LineValue): string {
    return value.uomOptions.find(option => option.uomId === value.uomId)?.code ?? value.baseUomCode;
  }

  private afterSubmit(document: DocumentDto): Observable<DocumentDto> {
    if (document.requiresApproval) {
      this.toast.success(this.i18n.t('doc.toast.sentForApproval'));
      this.afterChange(false);
      return of(document);
    }
    if (toDocumentActions(document.allowedActions).has(DOCUMENT_ACTION.Post)) {
      return this.postDocument(document, true);
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
        // A receipt from an order has no Add item button: the heading of the lines takes the focus.
        const target =
          invalid ??
          this.page.querySelector<HTMLElement>('.document-editor-lines-header button') ??
          this.page.querySelector<HTMLElement>('.document-editor-lines-header h2');
        target?.focus();
        // No line at all: the message sits under the Add item button; make sure it is on screen, not just the button (phone QA).
        if (!invalid) {
          this.page
            .querySelector<HTMLElement>('.document-editor-lines > .document-editor-error')
            ?.scrollIntoView?.({ block: 'nearest' });
        }
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
      replaceUrl: true,
      scroll: 'manual'
    });
  }

  /**
   * A command closes its dialog and then disables or removes the button that opened it, which leaves the focus on
   * <body> and a keyboard user back at the top of the page (EA-07). Move it to the error banner; a dialog that was dismissed
   * ran nothing, so its button is still there and gets the focus back WITHOUT scrolling (a tap on Back used to throw the page
   * to the top); else to the title.
   */
  private settleFocus(): void {
    const opener = this.opener;
    afterNextRender(
      () => {
        const active = this.page.activeElement;
        if (active && active !== this.page.body) {
          return;
        }
        // The error first (it comes after the title in the page, so one combined selector would pick the title).
        const banner = this.page.querySelector<HTMLElement>('app-banner[role="alert"][tabindex]');
        if (banner) {
          banner.focus();
          return;
        }
        if (opener?.isConnected && !opener.hasAttribute('disabled')) {
          opener.focus({ preventScroll: true });
          return;
        }
        this.page.querySelector<HTMLElement>('app-page-header h1')?.focus();
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
      // Posting or reversing moved what the order has received: show it, and the order's own page reads it again when it opens.
      if (this.$linkedOrder() !== undefined) {
        this.orders.detail.reload();
      }
    }
  }
}
