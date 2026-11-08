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
import { Router, RouterLink } from '@angular/router';
import { Observable, finalize, switchMap, tap } from 'rxjs';
import {
  ApprovalMetric,
  DocumentType,
  SalesOrderCloseKind,
  SalesOrderDto,
  SalesOrderLineDto,
  SalesOrderStatus
} from '@ims/api';
import { handledInline } from '@ims/core/http';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_STATUS_LABELS,
  EnumLabelPipe,
  I18nService,
  MessageKey,
  ROLE_LABELS,
  SALES_ORDER_STATUS_LABELS,
  TranslatePipe
} from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { PageTitleService } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { DOCUMENT_ROUTE, STATUS_TONE } from '@ims/features/documents';
import { isStaleOrderError, staleOrderDetails } from '@ims/features/purchasing';
import {
  SALES_ORDER_ROUTE,
  SO_ACTION,
  SO_STATUS_TONE,
  SalesOrdersStore,
  SoAction,
  isPrintable,
  toSalesOrderRef,
  toSoActions
} from '@ims/features/sales';
import {
  API_ERROR_KIND,
  ApiError,
  HTTP_STATUS,
  MIN_REASON_LENGTH,
  PERMISSION,
  PublicId
} from '@ims/models';
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

function isStateRefusal(error: ApiError | undefined): boolean {
  return (
    error !== undefined &&
    ((error.status === HTTP_STATUS.BadRequest && Object.keys(error.fieldErrors).length === 0) ||
      error.status === HTTP_STATUS.Forbidden)
  );
}

interface HistoryEvent {
  readonly labelKey: MessageKey;
  readonly by?: string;
  readonly at?: string;
  readonly reason?: string;
}

/**
 * One sales order: header, lines with ordered / delivered / remaining, deliveries, history and the buttons the API offers
 * (`allowedActions`, nothing decided here from the status). Prices, amounts and the total are absent for a person without
 * `so.manage`, and then there is no column or box for them. The editor and the delivery come in the next two slices.
 */
@Component({
  selector: 'app-sales-order-detail-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
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
  templateUrl: './sales-order-detail.component.html',
  styleUrl: './sales-order-detail.component.scss'
})
export class SalesOrderDetailPageComponent {
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private readonly pageTitle = inject(PageTitleService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly store = inject(SalesOrdersStore);
  private readonly toast = inject(ToastService);
  private readonly moneyPipe = new AppMoneyPipe();
  private readonly qtyPipe = new AppQtyPipe();

  /** Route parameter. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly action = SO_ACTION;
  readonly route = SALES_ORDER_ROUTE;
  readonly backLink = ['/', SALES_ORDER_ROUTE];
  readonly statusLabels = SALES_ORDER_STATUS_LABELS;
  readonly deliveryStatusLabels = DOCUMENT_STATUS_LABELS;
  readonly tone: Readonly<Record<string, StatusTone>> = SO_STATUS_TONE;
  readonly deliveryTone: Readonly<Record<string, StatusTone>> = STATUS_TONE;
  readonly deliveryRoute = DOCUMENT_ROUTE[DocumentType.Issue];

  /** The button the last action came from: a dismissed dialog hands the focus back to it. */
  private opener: HTMLElement | null = null;
  /** The button that was tapped shows the spinner, not whichever button is primary. */
  private readonly $activeAction = signal<SoAction | undefined>(undefined);

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

  /** What the API offers, nothing decided here from the status. */
  private readonly $actions = computed(() => toSoActions(this.$order()?.allowedActions));
  readonly $canEdit = computed(() => this.$actions().has(SO_ACTION.Edit));
  /** The customer copy shows selling prices: offered when the API sent them (so.manage), for an order that may be printed. */
  readonly $canPrint = computed(() => {
    const status = this.$order()?.status;
    return this.$hasMoney() && status !== undefined && isPrintable(status);
  });
  /** Offered by the API only when a delivery can be started from this order by this person (never decided here from the status). */
  readonly $canDeliver = computed(() => this.$actions().has(SO_ACTION.Deliver));
  readonly $canSubmit = computed(() => this.$actions().has(SO_ACTION.Submit));
  readonly $canRecall = computed(() => this.$actions().has(SO_ACTION.Recall));
  readonly $canApprove = computed(() => this.$actions().has(SO_ACTION.Approve));
  readonly $canReject = computed(() => this.$actions().has(SO_ACTION.Reject));
  readonly $canCancel = computed(() => this.$actions().has(SO_ACTION.Cancel));
  readonly $canClose = computed(() => this.$actions().has(SO_ACTION.Close));
  readonly $showBar = computed(() => this.$actions().size > 0);

  /** A dialog is open or about to be: a second tap on a button must not open another one. */
  private readonly $asking = signal(false);
  readonly $isBusy = computed(
    () =>
      this.$asking() ||
      this.store.submit.$isRunning() ||
      this.store.recall.$isRunning() ||
      this.store.approve.$isRunning() ||
      this.store.reject.$isRunning() ||
      this.store.cancel.$isRunning() ||
      this.store.close.$isRunning() ||
      this.store.deliver.$isRunning()
  );
  readonly $busyAction = computed(() => (this.$isBusy() ? this.$activeAction() : undefined));

  /** The last error of each command; a command that changes the order clears the others' (the store does that). */
  private readonly $commandErrors = computed<readonly (ApiError | undefined)[]>(() => [
    this.store.submit.$operation().error,
    this.store.recall.$operation().error,
    this.store.approve.$operation().error,
    this.store.reject.$operation().error,
    this.store.cancel.$operation().error,
    this.store.close.$operation().error,
    this.store.deliver.$operation().error
  ]);
  /** Someone changed the order since it was opened: nothing can be done with this copy until it is read again. */
  readonly $isStale = computed(() => this.$commandErrors().some(isStaleOrderError));
  readonly $staleDetails = computed(() =>
    this.$commandErrors().map(staleOrderDetails).find(isDefined)
  );
  /** A refusal the interceptor does not toast (a 409 on an If-Match call): the API's own sentence, shown as an alert. */
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

  /** A delivery opens for a person who may read stock; others read its number and state. */
  readonly $canOpenDeliveries = computed(() => this.permissions.has(PERMISSION.StockRead));
  /** Money is on the order only for a holder of so.manage: the API leaves the key out for the others. */
  readonly $hasMoney = computed(() => this.$order()?.totalAmount !== undefined);
  readonly $returnedBy = computed(() => {
    const order = this.$order();
    return order ? (order.returnedByName ?? order.returnedBy ?? '') : '';
  });
  readonly $isCancelled = computed(() => this.$order()?.status === SalesOrderStatus.Cancelled);
  readonly $closeText = computed<MessageKey | undefined>(() => {
    const kind = this.$order()?.closeKind;
    if (!isDefined(kind)) {
      return undefined;
    }
    return kind === SalesOrderCloseKind.Short ? 'so.closedShort' : 'so.closedDelivered';
  });
  /** Why an approval rule held the order, in words; a caller without so.manage gets the kind of rule, not the numbers. */
  readonly $approvalTriggers = computed(() =>
    (this.$order()?.approvalTriggers ?? []).map(trigger => {
      const isAmount = trigger.metric === ApprovalMetric.Amount;
      const known = isDefined(trigger.actual) && isDefined(trigger.threshold);
      const format = (value: number | undefined): string =>
        isAmount ? this.moneyPipe.transform(value) : this.qtyPipe.transform(value);
      const amountKey: MessageKey = known ? 'doc.trigger.Amount' : 'doc.trigger.AmountHidden';
      const key: MessageKey = isAmount ? amountKey : 'doc.trigger.Quantity';
      return {
        key,
        actual: known ? format(trigger.actual) : '',
        threshold: known ? format(trigger.threshold) : '',
        role: this.i18n.enumLabel(ROLE_LABELS, trigger.roleRequired)
      };
    })
  );
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
        ? { labelKey: 'so.history.closed', at: order.closedAt, reason: order.closeReason }
        : undefined,
      order.status === SalesOrderStatus.Cancelled
        ? { labelKey: 'doc.history.cancelled', reason: order.cancelReason ?? undefined }
        : undefined
    ];
    // Oldest first; an event without a time (the API has no cancel time) stays last.
    const time = (event: HistoryEvent): number => (event.at ? Date.parse(event.at) : Infinity);
    return events.filter(isDefined).sort((a, b) => {
      const [x, y] = [time(a), time(b)];
      if (x === y) {
        return 0;
      }
      return x < y ? -1 : 1;
    });
  });
  readonly $lineColumns = computed<readonly TableColumn<SalesOrderLineDto>[]>(() => [
    {
      key: 'sku',
      type: 'code',
      header: this.i18n.t('product.sku'),
      value: line => line.sku,
      primary: true
    },
    { key: 'name', header: this.i18n.t('product.name'), value: line => line.productName },
    { key: 'qty', header: this.i18n.t('po.col.ordered'), align: 'end' },
    { key: 'delivered', header: this.i18n.t('so.col.delivered'), align: 'end' },
    { key: 'remaining', header: this.i18n.t('po.col.remaining'), align: 'end' },
    ...columnWhen<SalesOrderLineDto>(this.$hasMoney(), {
      key: 'price',
      header: this.i18n.t('po.col.price'),
      align: 'end',
      hideOnPhone: true
    }),
    ...columnWhen<SalesOrderLineDto>(this.$hasMoney(), {
      key: 'amount',
      header: this.i18n.t('doc.amount'),
      align: 'end'
    })
  ]);

  /** Opens what the route names; also when the same page is reused for another order. */
  private readonly openOrder = effect(() => {
    const id = this.$orderId();
    untracked(() => {
      // The store outlives this page (and a sign-out): what the last order's commands failed with is not this order's.
      this.store.resetCommands();
      this.store.detail.reset();
      if (id !== undefined) {
        this.store.detail.load(id, handledInline());
      }
    });
  });

  /** A failed load replaces the page with one banner: the keyboard and the screen reader land on it. */
  private readonly focusLoadFailure = effect(() => {
    if (this.$loadFailed() && !this.$notFound()) {
      untracked(() => this.settleFocus());
    }
  });

  private readonly syncTitle = effect(() => this.pageTitle.setDetail(this.$order()?.no));

  /**
   * A command refused because the order is not in a state that allows it (a plain 400) or the caller may not do it any more (403):
   * the sentence was announced once by the toast, and the page is read again so its buttons match the order. The failed command is
   * forgotten first, or any later command would run this again for the old error and race its own answer.
   */
  private readonly reloadAfterRefusal = effect(() => {
    if (this.$commandErrors().some(isStateRefusal)) {
      untracked(() => {
        this.store.resetFailed(isStateRefusal);
        this.store.detail.reload();
      });
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

  onAction(action: SoAction): void {
    const order = this.$order();
    if (!order || this.$isBusy()) {
      return;
    }
    this.opener = this.page.activeElement instanceof HTMLElement ? this.page.activeElement : null;
    this.$activeAction.set(action);
    const ref = toSalesOrderRef(order);
    switch (action) {
      case SO_ACTION.Submit:
        this.store.submit
          .run(ref)
          .pipe(
            tap(result =>
              this.toast.success(
                this.i18n.t(
                  result.status === SalesOrderStatus.Approved
                    ? 'po.toast.released'
                    : 'doc.toast.sentForApproval'
                )
              )
            ),
            finalize(() => this.settleFocus()),
            takeUntilDestroyed(this.destroyRef)
          )
          .subscribe();
        break;
      case SO_ACTION.Recall:
        // The submitter takes the order back to Draft; asked first (a submitted order leaves the approval queue).
        this.run(
          {
            heading: this.i18n.t('dialog.recall.title', { docNo: order.no }),
            body: this.i18n.t(
              order.requiresApproval ? 'dialog.recall.body' : 'dialog.recall.bodyReady'
            ),
            confirmLabel: this.i18n.t('dialog.recall.confirm'),
            cancelLabel: this.i18n.t(
              order.requiresApproval ? 'dialog.recall.keep' : 'dialog.recall.keepReady'
            )
          },
          () => this.store.recall.run(ref),
          'po.toast.recalled'
        );
        break;
      case SO_ACTION.Approve:
        this.run(
          {
            heading: this.i18n.t('dialog.approve.title', { docNo: order.no }),
            body: this.i18n.t('so.dialog.approve.body'),
            confirmLabel: this.i18n.t('doc.action.approve'),
            cancelLabel: this.i18n.t('common.back')
          },
          () => this.store.approve.run(ref),
          'doc.toast.approved'
        );
        break;
      case SO_ACTION.Reject: {
        const name = order.createdByName || order.createdBy;
        this.run(
          {
            heading: this.i18n.t('dialog.reject.title', { docNo: order.no }),
            body: this.i18n.t('dialog.reject.body', { name }),
            confirmLabel: this.i18n.t('doc.action.reject'),
            cancelLabel: this.i18n.t('common.back'),
            destructive: true,
            reasonLabel: this.i18n.t('dialog.reason'),
            reasonRequired: true,
            minReasonLength: MIN_REASON_LENGTH
          },
          reason => this.store.reject.run({ ...ref, reason }),
          'doc.toast.returned',
          { name }
        );
        break;
      }
      case SO_ACTION.Cancel: {
        // Cancelling an approved order undoes someone's decision: the reason is required there (the API refuses without).
        const approved = order.status === SalesOrderStatus.Approved;
        this.run(
          {
            heading: this.i18n.t('po.dialog.cancel.title', { docNo: order.no }),
            body: this.i18n.t('po.dialog.cancel.body'),
            confirmLabel: this.i18n.t('po.action.cancel'),
            cancelLabel: this.i18n.t('po.dialog.cancel.keep'),
            destructive: true,
            reasonLabel: this.i18n.t(approved ? 'dialog.reason' : 'dialog.reasonOptional'),
            ...(approved && { reasonRequired: true, minReasonLength: MIN_REASON_LENGTH })
          },
          reason => this.store.cancel.run({ ...ref, reason }),
          'po.toast.cancelled'
        );
        break;
      }
      case SO_ACTION.Close:
        // Close short: what was not delivered will not go. The reason is required and kept on the order.
        this.run(
          {
            heading: this.i18n.t('po.dialog.close.title', { docNo: order.no }),
            body: this.i18n.t('so.dialog.close.body'),
            confirmLabel: this.i18n.t('po.action.close'),
            cancelLabel: this.i18n.t('common.back'),
            destructive: true,
            reasonLabel: this.i18n.t('dialog.reason'),
            reasonRequired: true,
            minReasonLength: MIN_REASON_LENGTH
          },
          reason => this.store.close.run({ ...ref, reason }),
          'po.toast.closed'
        );
        break;
      case SO_ACTION.Deliver:
        this.deliver(order.id);
        break;
      default:
        // Edit is a link to the editor, not a command.
        break;
    }
  }

  /** Starts the draft delivery and opens it in the document editor; a refusal is the alert above, and the order is read again so the buttons match. */
  private deliver(id: PublicId): void {
    this.store.deliver
      .run(id)
      .pipe(
        tap(
          delivery =>
            void this.router.navigate(['/', DOCUMENT_ROUTE[DocumentType.Issue], delivery.id])
        ),
        finalize(() => {
          if (this.store.deliver.$operation().error?.kind === API_ERROR_KIND.Conflict) {
            this.store.detail.reload();
          }
          this.settleFocus();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /** Asks first, then runs the command with the reason entered, tells the person, and settles the focus. */
  private run(
    request: Parameters<ConfirmService['ask']>[0],
    command: (reason: string) => Observable<SalesOrderDto>,
    toastKey: MessageKey,
    toastParams?: Record<string, string>
  ): void {
    this.$asking.set(true);
    this.confirm
      .ask(request)
      .pipe(
        switchMap(reason => command(reason)),
        tap(() => this.toast.success(this.i18n.t(toastKey, toastParams))),
        finalize(() => {
          this.$asking.set(false);
          this.settleFocus();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /**
   * A command closes its dialog and then disables or removes the button that opened it, which leaves the focus on <body> and a
   * keyboard user back at the top of the page. Move it to the error banner; a dialog that was dismissed ran nothing, so its button
   * is still there and gets the focus back without scrolling; else to the title. (Same workaround as the purchase order page.)
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
