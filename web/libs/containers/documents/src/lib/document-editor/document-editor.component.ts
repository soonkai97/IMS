import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
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
import { HasUnsavedChanges } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { SystemSettingsStore } from '@ims/core/settings';
import { ApprovalsStore } from '@ims/features/approvals';
import { CatalogStore } from '@ims/features/catalog';
import { DOCUMENT_ROUTE, DocumentsStore, LineGroup, STATUS_TONE } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-editor.component.html',
  styleUrl: './document-editor.component.scss'
})
export class DocumentEditorPageComponent implements OnInit, HasUnsavedChanges {
  private readonly approvals = inject(ApprovalsStore);
  private readonly catalog = inject(CatalogStore);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly documents = inject(DocumentsStore);
  private readonly i18n = inject(I18nService);
  private readonly partners = inject(PartnersStore);
  private readonly permissions = inject(PermissionService);
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

  private destroyed = false;
  private savedDocumentId: number | undefined;

  $tab = signal<string>(TAB_ITEMS);
  $addItemOpen = signal(false);
  $lastAdded = signal<string | undefined>(undefined);
  $showLineError = signal(false);

  readonly $documentId = computed(() => {
    const id = this.$id();
    return id === undefined ? undefined : Number(id);
  });
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
  readonly $canPost = computed(() => this.permissions.has(PERMISSION.DocumentPost));
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
    return this.i18n.t(this.$isIssue() ? 'issues.new' : 'receipts.new');
  });
  readonly $partnerLabelKey = computed<MessageKey>(() =>
    this.$isIssue() ? 'doc.customer' : 'doc.supplier'
  );

  readonly $returnedReason = computed(() => {
    const document = this.$document();
    return document?.status === DocumentStatus.Draft ? document.rejectionReason : undefined;
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
      { labelKey: 'doc.history.created', by: document.createdBy, at: document.createdAt },
      document.submittedAt
        ? { labelKey: 'doc.history.submitted', by: document.submittedBy, at: document.submittedAt }
        : undefined,
      document.rejectionReason
        ? { labelKey: 'doc.history.returned', reason: document.rejectionReason }
        : undefined,
      document.approvedAt
        ? { labelKey: 'doc.history.approved', by: document.approvedBy, at: document.approvedAt }
        : undefined,
      document.negativeStockOverrideReason
        ? { labelKey: 'doc.history.override', reason: document.negativeStockOverrideReason }
        : undefined,
      document.postedAt
        ? { labelKey: 'doc.history.posted', by: document.postedBy, at: document.postedAt }
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
      value: row => row.warehouseCode,
      hideOnPhone: true
    }
  ]);
  /** A rejected submit / post because stock moved (409 with product, available, requested): E5. */
  readonly $stockConflict = computed(() => {
    const errors = [
      this.documents.submit.$operation().error,
      this.documents.post.$operation().error,
      this.documents.approve.$operation().error
    ];
    const extensions = errors.find(
      error => error?.extensions['productId'] !== undefined
    )?.extensions;
    if (!extensions) {
      return undefined;
    }
    const productId = Number(extensions['productId']);
    const line = this.$values().find(value => value.productId === productId);
    return {
      sku: line?.sku ?? String(productId),
      available: Number(extensions['available'] ?? 0),
      requested: Number(extensions['requested'] ?? 0),
      uom: line?.baseUomCode ?? ''
    };
  });
  readonly $saveGeneralErrors = computed(() => {
    const error = this.documents.save.$operation().error;
    if (!error) {
      return [];
    }
    return Object.entries(error.fieldErrors)
      .filter(([field]) => !this.view.header.get(field) && !field.startsWith('lines'))
      .flatMap(([, messages]) => messages);
  });

  private readonly populateForm = effect(() => {
    const document = this.$document();
    const editable = this.$editable();
    if (!document) {
      return;
    }
    untracked(() => {
      this.view.populate(document, editable);
      if (editable && document.type === DocumentType.Issue) {
        this.view
          .refreshAvailability(document.warehouseId)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe();
      }
    });
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
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
    });
  }

  ngOnInit(): void {
    this.view.configure(this.$type());
    this.catalog.ensureLoaded();
    this.warehouses.ensureLoaded();
    this.settings.ensureLoaded();
    this.partners.loadActive(this.$isIssue() ? PartnerType.Customer : PartnerType.Supplier);

    const documentId = this.$documentId();
    if (documentId === undefined) {
      this.documents.detail.reset();
      this.view.startNew(null);
    } else {
      this.documents.detail.load(documentId);
    }

    this.view.header.controls.warehouseId.valueChanges
      .pipe(
        filter(isDefined),
        filter(() => this.$isIssue() && this.$editable()),
        switchMap(warehouseId => this.view.refreshAvailability(warehouseId)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  hasUnsavedChanges(): boolean {
    return this.$editable() && this.view.hasUnsavedChanges();
  }

  onTabChange(tab: string): void {
    this.$tab.set(tab);
  }

  onOpenAddItem(): void {
    this.$lastAdded.set(undefined);
    this.$addItemOpen.set(true);
  }

  onProductPicked(picked: PickedProduct): void {
    this.view.addProduct(picked.product, picked.available);
    this.$lastAdded.set(this.i18n.t('doc.itemAdded', { sku: picked.product.sku }));
  }

  onStep(event: { group: LineGroup; delta: number }): void {
    this.view.step(event.group, event.delta);
  }

  onRemoveLine(index: number): void {
    this.view.remove(index);
  }

  onSetToAvailable(group: LineGroup): void {
    this.view.setToAvailable(group);
  }

  onRetryLoad(): void {
    this.documents.detail.reload();
  }

  onAction(action: DocumentAction): void {
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
        this.runPost(this.$document()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
        break;
      case DOCUMENT_ACTION.Reverse:
        this.reverse();
        break;
    }
  }

  private saveDraft(): void {
    if (!this.validate()) {
      return;
    }
    this.save()
      .pipe(
        tap(() => this.toast.success(this.i18n.t('doc.toast.draftSaved'))),
        finalize(() => this.openSavedDocument()),
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
        switchMap(document => this.documents.submit.run(document.id)),
        switchMap(document => this.afterSubmit(document)),
        finalize(() => this.openSavedDocument()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private approve(): void {
    const document = this.$document();
    if (!document) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.approve.title', { docNo: document.no }),
        body: this.i18n.t('dialog.approve.body'),
        confirmLabel: this.i18n.t('doc.action.approve'),
        cancelLabel: this.i18n.t('common.back')
      })
      .pipe(
        switchMap(() => this.documents.approve.run(document.id)),
        tap(() => this.toast.success(this.i18n.t('doc.toast.approved'))),
        tap(() => this.afterChange(false)),
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
        body: this.i18n.t('dialog.reject.body', { name: document.createdBy }),
        confirmLabel: this.i18n.t('doc.action.reject'),
        cancelLabel: this.i18n.t('common.back'),
        destructive: true,
        reasonLabel: this.i18n.t('dialog.reason'),
        reasonRequired: true,
        minReasonLength: MIN_REASON_LENGTH
      })
      .pipe(
        switchMap(reason => this.documents.reject.run({ id: document.id, reason })),
        tap(() =>
          this.toast.success(this.i18n.t('doc.toast.returned', { name: document.createdBy }))
        ),
        tap(() => this.afterChange(false)),
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
        switchMap(reason => this.documents.cancel.run({ id: document.id, reason })),
        tap(() => this.toast.success(this.i18n.t('doc.toast.cancelled'))),
        tap(() => this.afterChange(false)),
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
        switchMap(reason => this.documents.reverse.run({ id: document.id, reason })),
        tap(() => this.toast.success(this.i18n.t('doc.toast.reversed'))),
        tap(() => this.afterChange(true)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Confirm, then post. The optional reason is the negative-stock override reason of an issue. */
  private runPost(document: DocumentDto | undefined): Observable<DocumentDto> {
    if (!document) {
      return of();
    }
    const direction = document.type === DocumentType.Issue ? 'decrease' : 'increase';
    return this.confirm
      .ask({
        heading: this.i18n.t('dialog.post.title', { docNo: document.no }),
        body: this.i18n.t(
          direction === 'decrease' ? 'dialog.post.bodyDecrease' : 'dialog.post.bodyIncrease',
          {
            count: document.lines.length
          }
        ),
        confirmLabel: this.i18n.t('doc.action.post'),
        cancelLabel: this.i18n.t('common.back'),
        reasonLabel:
          document.type === DocumentType.Issue && this.$allowNegativeStock()
            ? this.i18n.t('dialog.post.overrideReason')
            : undefined
      })
      .pipe(
        switchMap(reason =>
          this.documents.post.run({ id: document.id, negativeStockOverrideReason: reason })
        ),
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
    return this.documents.save
      .run({ type: this.$type(), id: this.$documentId(), body: this.view.toRequest() })
      .pipe(tap(document => (this.savedDocumentId = document.id)));
  }

  private validate(): boolean {
    this.view.markAllTouched();
    this.$showLineError.set(this.$noLines());
    return this.view.isValid();
  }

  /** A new document gets its own URL once the whole save / submit / post chain has finished. */
  private openSavedDocument(): void {
    if (this.destroyed || !this.$isNew() || this.savedDocumentId === undefined) {
      return;
    }
    void this.router.navigate(['/', DOCUMENT_ROUTE[this.$type()], this.savedDocumentId], {
      replaceUrl: true
    });
  }

  /** Keeps menu badges and the dashboard honest after a state change. */
  private afterChange(stockChanged: boolean): void {
    if (this.permissions.has(PERMISSION.DocumentApprove)) {
      this.approvals.refresh();
    }
    if (stockChanged) {
      this.stock.summary.load();
    }
  }
}
