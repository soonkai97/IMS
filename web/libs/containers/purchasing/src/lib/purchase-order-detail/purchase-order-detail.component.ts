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
import { RouterLink } from '@angular/router';
import { finalize, switchMap, tap } from 'rxjs';
import { PurchaseOrderCloseKind, PurchaseOrderLineDto, PurchaseOrderStatus } from '@ims/api';
import { handledInline } from '@ims/core/http';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_STATUS_LABELS,
  EnumLabelPipe,
  I18nService,
  MessageKey,
  PURCHASE_ORDER_STATUS_LABELS,
  TranslatePipe
} from '@ims/core/i18n';
import { CanDirective, PermissionService } from '@ims/core/identity';
import { PageTitleService } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { STATUS_TONE } from '@ims/features/documents';
import {
  PO_ACTION,
  PO_STATUS_TONE,
  PURCHASE_ORDER_ROUTE,
  PoAction,
  PurchaseOrdersStore,
  isStaleOrderError,
  staleOrderDetails,
  toPoActions,
  toPurchaseOrderRef
} from '@ims/features/purchasing';
import { API_ERROR_KIND, ApiError, HTTP_STATUS, MIN_REASON_LENGTH, PERMISSION } from '@ims/models';
import {
  StatusTone,
  TableColumn,
  columnWhen,
  UiBannerComponent,
  UiButtonComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPublishHeightDirective,
  UiSkeletonComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { isDefined, normalizePublicId } from '@ims/utils';

interface HistoryEvent {
  readonly labelKey: MessageKey;
  readonly by?: string;
  readonly at?: string;
  readonly reason?: string;
}

/**
 * One purchase order, read only: header, lines with ordered / received / remaining, linked receipts and history. Prices, amounts
 * and the total are the API's null for a person without `cost.view`, and then there is no column or box for them. The buttons come
 * in a later slice.
 */
@Component({
  selector: 'app-purchase-order-detail-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
    CanDirective,
    EnumLabelPipe,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCellDirective,
    UiEmptyStateComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPublishHeightDirective,
    UiSkeletonComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './purchase-order-detail.component.html',
  styleUrl: './purchase-order-detail.component.scss'
})
export class PurchaseOrderDetailPageComponent {
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private readonly pageTitle = inject(PageTitleService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(PurchaseOrdersStore);
  private readonly toast = inject(ToastService);

  /** Route parameter. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly action = PO_ACTION;
  readonly backLink = ['/', PURCHASE_ORDER_ROUTE];
  readonly statusLabels = PURCHASE_ORDER_STATUS_LABELS;
  readonly receiptStatusLabels = DOCUMENT_STATUS_LABELS;
  readonly tone: Readonly<Record<string, StatusTone>> = PO_STATUS_TONE;
  readonly receiptTone: Readonly<Record<string, StatusTone>> = STATUS_TONE;

  /** The button the last action came from: a dismissed dialog hands the focus back to it. */
  private opener: HTMLElement | null = null;
  /** The button that was tapped shows the spinner, not whichever button is primary. */
  private readonly $activeAction = signal<PoAction | undefined>(undefined);

  /** The id in the URL; undefined when it is not a GUID (an old bookmark), which is a not-found page and no request. */
  private readonly $orderId = computed(() => normalizePublicId(this.$id()));
  private readonly $unusableId = computed(
    () => this.$id() !== undefined && this.$orderId() === undefined
  );
  readonly $order = computed(() => {
    const order = this.store.detail.$data();
    return order && order.id === this.$orderId() ? order : undefined;
  });
  readonly $isLoading = computed(
    () => this.store.detail.$isLoading() && this.$order() === undefined
  );
  readonly $loadFailed = this.store.detail.$isFailed;
  /** The load call raises no toast; this banner is the one announcement, with the sentence the interceptor mapped. */
  readonly $loadErrorMessage = computed(() => this.store.detail.$operation().error?.message);
  /** Retrying helps for a network or server failure, not for a refusal (403) or a missing order. */
  readonly $canRetry = computed(() => {
    const kind = this.store.detail.$operation().error?.kind;
    return kind === API_ERROR_KIND.Network || kind === API_ERROR_KIND.Server;
  });
  readonly $notFound = computed(
    () =>
      this.$unusableId() || this.store.detail.$operation().error?.kind === API_ERROR_KIND.NotFound
  );
  readonly $heading = computed(
    () => this.$order()?.no ?? this.i18n.t(this.$notFound() ? 'notFound.title' : 'ui.loading')
  );
  private readonly $actions = computed(() => toPoActions(this.$order()?.allowedActions));
  readonly $canSubmit = computed(() => this.$actions().has(PO_ACTION.Submit));
  readonly $canRecall = computed(() => this.$actions().has(PO_ACTION.Recall));
  readonly $canApprove = computed(() => this.$actions().has(PO_ACTION.Approve));
  readonly $canReject = computed(() => this.$actions().has(PO_ACTION.Reject));
  readonly $canCancel = computed(() => this.$actions().has(PO_ACTION.Cancel));
  readonly $canClose = computed(() => this.$actions().has(PO_ACTION.Close));
  readonly $showBar = computed(
    () =>
      this.$canSubmit() ||
      this.$canRecall() ||
      this.$canApprove() ||
      this.$canReject() ||
      this.$canCancel() ||
      this.$canClose()
  );

  readonly $isBusy = computed(
    () =>
      this.store.submit.$isRunning() ||
      this.store.recall.$isRunning() ||
      this.store.approve.$isRunning() ||
      this.store.reject.$isRunning() ||
      this.store.cancel.$isRunning() ||
      this.store.close.$isRunning()
  );
  readonly $busyAction = computed(() => (this.$isBusy() ? this.$activeAction() : undefined));

  /** The last error of each command; a command that changes the order clears the others' (the store does that). */
  private readonly $commandErrors = computed<readonly (ApiError | undefined)[]>(() => [
    this.store.submit.$operation().error,
    this.store.recall.$operation().error,
    this.store.approve.$operation().error,
    this.store.reject.$operation().error,
    this.store.cancel.$operation().error,
    this.store.close.$operation().error
  ]);
  /** Someone changed the order since it was opened: nothing can be done with this copy until it is read again. */
  readonly $isStale = computed(() => this.$commandErrors().some(isStaleOrderError));
  readonly $staleDetails = computed(() =>
    this.$commandErrors().map(staleOrderDetails).find(isDefined)
  );
  /**
   * A refusal the interceptor does not toast (a 409 on an If-Match call, a 400 with field errors): the API's own sentence, shown
   * as an alert. The stale 409 has its banner and Reload button instead.
   */
  readonly $refusal = computed(() => {
    const error = this.$commandErrors().find(
      candidate =>
        candidate !== undefined &&
        !isStaleOrderError(candidate) &&
        (candidate.kind === API_ERROR_KIND.Conflict ||
          Object.keys(candidate.fieldErrors).length > 0)
    );
    return error ? Object.values(error.fieldErrors).flat().join(' ') || error.message : undefined;
  });

  readonly $canViewCost = computed(() => this.permissions.has(PERMISSION.CostView));
  /** A receipt opens for a person who may read stock; others read its number and state. */
  readonly $canOpenReceipts = computed(() => this.permissions.has(PERMISSION.StockRead));
  readonly $returnedBy = computed(() => {
    const order = this.$order();
    return order ? (order.returnedByName ?? order.returnedBy ?? '') : '';
  });
  readonly $isCancelled = computed(() => this.$order()?.status === PurchaseOrderStatus.Cancelled);
  readonly $closeText = computed<MessageKey | undefined>(() => {
    const kind = this.$order()?.closeKind;
    // The API sends null, not an absent member, for what an open order does not have.
    if (!isDefined(kind)) {
      return undefined;
    }
    return kind === PurchaseOrderCloseKind.Short ? 'po.closedShort' : 'po.closedReceived';
  });
  readonly $history = computed<readonly HistoryEvent[]>(() => {
    const order = this.$order();
    if (!order) {
      return [];
    }
    const events: (HistoryEvent | undefined)[] = [
      { labelKey: 'doc.history.created', by: order.createdByName, at: order.createdAt },
      order.submittedAt
        ? {
            labelKey: 'doc.history.submitted',
            by: order.submittedByName ?? order.submittedBy,
            at: order.submittedAt
          }
        : undefined,
      order.rejectionReason
        ? {
            labelKey: 'doc.history.returned',
            by: order.returnedByName ?? order.returnedBy,
            at: order.returnedAt,
            reason: order.rejectionReason
          }
        : undefined,
      order.approvedAt
        ? {
            labelKey: 'doc.history.approved',
            by: order.approvedByName ?? order.approvedBy,
            at: order.approvedAt
          }
        : undefined,
      order.closedAt
        ? { labelKey: 'po.history.closed', at: order.closedAt, reason: order.closeReason }
        : undefined,
      order.status === PurchaseOrderStatus.Cancelled
        ? { labelKey: 'doc.history.cancelled', reason: order.cancelReason ?? undefined }
        : undefined
    ];
    // Oldest first; an event without a time (the API has no cancel time) stays last.
    const time = (event: HistoryEvent): number => (event.at ? Date.parse(event.at) : Infinity);
    return events.filter(isDefined).sort((a, b) => time(a) - time(b));
  });
  readonly $lineColumns = computed<readonly TableColumn<PurchaseOrderLineDto>[]>(() => [
    {
      key: 'sku',
      type: 'code',
      header: this.i18n.t('product.sku'),
      value: line => line.sku,
      primary: true
    },
    { key: 'name', header: this.i18n.t('product.name'), value: line => line.productName },
    { key: 'qty', header: this.i18n.t('po.col.ordered'), align: 'end' },
    { key: 'received', header: this.i18n.t('po.col.received'), align: 'end' },
    { key: 'remaining', header: this.i18n.t('po.col.remaining'), align: 'end' },
    ...columnWhen<PurchaseOrderLineDto>(this.$canViewCost(), {
      key: 'price',
      header: this.i18n.t('po.col.price'),
      align: 'end',
      hideOnPhone: true
    }),
    ...columnWhen<PurchaseOrderLineDto>(this.$canViewCost(), {
      key: 'amount',
      header: this.i18n.t('doc.amount'),
      align: 'end'
    })
  ]);

  /** Opens what the route names; also when the same page is reused for another order. */
  private readonly openOrder = effect(() => {
    const id = this.$orderId();
    untracked(() => {
      this.store.detail.reset();
      if (id !== undefined) {
        this.store.detail.load(id, handledInline());
      }
    });
  });

  private readonly syncTitle = effect(() => this.pageTitle.setDetail(this.$order()?.no));

  /**
   * A command refused with a plain 400 (the order is no longer in a state that allows it): the sentence was announced once by
   * the toast, and the page is read again so its buttons match the order.
   */
  private readonly reloadAfterWrongState = effect(() => {
    const refused = this.$commandErrors().some(
      error =>
        error?.status === HTTP_STATUS.BadRequest && Object.keys(error.fieldErrors).length === 0
    );
    if (refused) {
      untracked(() => this.store.detail.reload());
    }
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.pageTitle.setDetail(undefined));
  }

  onRetryLoad(): void {
    this.store.detail.reload();
  }

  /** E4: drop the outdated copy and read the order again. */
  onReload(): void {
    this.store.resetCommands();
    this.store.detail.reload();
  }

  onAction(action: PoAction): void {
    this.opener = this.page.activeElement instanceof HTMLElement ? this.page.activeElement : null;
    this.$activeAction.set(action);
    switch (action) {
      case PO_ACTION.Submit:
        this.submit();
        break;
      case PO_ACTION.Recall:
        this.recall();
        break;
      case PO_ACTION.Approve:
        this.approve();
        break;
      case PO_ACTION.Reject:
        this.reject();
        break;
      case PO_ACTION.Cancel:
        this.cancel();
        break;
      case PO_ACTION.Close:
        this.close();
        break;
      case PO_ACTION.Edit:
        // The editor is a later slice; the button is not rendered.
        break;
    }
  }

  private submit(): void {
    const order = this.$order();
    if (!order) {
      return;
    }
    this.store.submit
      .run(toPurchaseOrderRef(order))
      .pipe(
        tap(result =>
          this.toast.success(
            this.i18n.t(
              result.status === PurchaseOrderStatus.Approved
                ? 'po.toast.released'
                : 'doc.toast.sentForApproval'
            )
          )
        ),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** The submitter takes the order back to Draft; the person is asked first (a submitted order leaves the approval queue). */
  private recall(): void {
    const order = this.$order();
    if (!order) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.recall.title', { docNo: order.no }),
        body: this.i18n.t(
          order.requiresApproval ? 'dialog.recall.body' : 'dialog.recall.bodyReady'
        ),
        confirmLabel: this.i18n.t('dialog.recall.confirm'),
        cancelLabel: this.i18n.t(
          order.requiresApproval ? 'dialog.recall.keep' : 'dialog.recall.keepReady'
        )
      })
      .pipe(
        switchMap(() => this.store.recall.run(toPurchaseOrderRef(order))),
        tap(() => this.toast.success(this.i18n.t('po.toast.recalled'))),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private approve(): void {
    const order = this.$order();
    if (!order) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.approve.title', { docNo: order.no }),
        body: this.i18n.t('po.dialog.approve.body'),
        confirmLabel: this.i18n.t('doc.action.approve'),
        cancelLabel: this.i18n.t('common.back')
      })
      .pipe(
        switchMap(() => this.store.approve.run(toPurchaseOrderRef(order))),
        tap(() => this.toast.success(this.i18n.t('doc.toast.approved'))),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private reject(): void {
    const order = this.$order();
    if (!order) {
      return;
    }
    const name = order.createdByName || order.createdBy;
    this.confirm
      .ask({
        heading: this.i18n.t('dialog.reject.title', { docNo: order.no }),
        body: this.i18n.t('dialog.reject.body', { name }),
        confirmLabel: this.i18n.t('doc.action.reject'),
        cancelLabel: this.i18n.t('common.back'),
        destructive: true,
        reasonLabel: this.i18n.t('dialog.reason'),
        reasonRequired: true,
        minReasonLength: MIN_REASON_LENGTH
      })
      .pipe(
        switchMap(reason => this.store.reject.run({ ...toPurchaseOrderRef(order), reason })),
        tap(() => this.toast.success(this.i18n.t('doc.toast.returned', { name }))),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private cancel(): void {
    const order = this.$order();
    if (!order) {
      return;
    }
    // Cancelling an approved order undoes someone's decision: the reason is required there (the API refuses without).
    const approved = order.status === PurchaseOrderStatus.Approved;
    this.confirm
      .ask({
        heading: this.i18n.t('po.dialog.cancel.title', { docNo: order.no }),
        body: this.i18n.t('po.dialog.cancel.body'),
        confirmLabel: this.i18n.t('po.action.cancel'),
        cancelLabel: this.i18n.t('po.dialog.cancel.keep'),
        destructive: true,
        reasonLabel: this.i18n.t(approved ? 'dialog.reason' : 'dialog.reasonOptional'),
        ...(approved && { reasonRequired: true, minReasonLength: MIN_REASON_LENGTH })
      })
      .pipe(
        switchMap(reason => this.store.cancel.run({ ...toPurchaseOrderRef(order), reason })),
        tap(() => this.toast.success(this.i18n.t('po.toast.cancelled'))),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Close short: what was not received will not come. The reason is required and kept on the order. */
  private close(): void {
    const order = this.$order();
    if (!order) {
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('po.dialog.close.title', { docNo: order.no }),
        body: this.i18n.t('po.dialog.close.body'),
        confirmLabel: this.i18n.t('po.action.close'),
        cancelLabel: this.i18n.t('common.back'),
        destructive: true,
        reasonLabel: this.i18n.t('dialog.reason'),
        reasonRequired: true,
        minReasonLength: MIN_REASON_LENGTH
      })
      .pipe(
        switchMap(reason => this.store.close.run({ ...toPurchaseOrderRef(order), reason })),
        tap(() => this.toast.success(this.i18n.t('po.toast.closed'))),
        finalize(() => this.settleFocus()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /**
   * A command closes its dialog and then disables or removes the button that opened it, which leaves the focus on <body> and a
   * keyboard user back at the top of the page. Move it to the error banner; a dialog that was dismissed ran nothing, so its button
   * is still there and gets the focus back without scrolling; else to the title. (The shared ConfirmService does not restore the
   * focus yet; this is the same workaround as the document editor's.)
   */
  private settleFocus(): void {
    const opener = this.opener;
    afterNextRender(
      () => {
        const active = this.page.activeElement;
        if (active && active !== this.page.body) {
          return;
        }
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
}
