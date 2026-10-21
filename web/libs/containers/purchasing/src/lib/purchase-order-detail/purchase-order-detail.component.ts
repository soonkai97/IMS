import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  untracked
} from '@angular/core';
import { RouterLink } from '@angular/router';
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
import { STATUS_TONE } from '@ims/features/documents';
import {
  PO_STATUS_TONE,
  PURCHASE_ORDER_ROUTE,
  PurchaseOrdersStore
} from '@ims/features/purchasing';
import { API_ERROR_KIND, PERMISSION } from '@ims/models';
import {
  StatusTone,
  TableColumn,
  columnWhen,
  UiBannerComponent,
  UiButtonComponent,
  UiCellDirective,
  UiEmptyStateComponent,
  UiPageHeaderComponent,
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
    UiPageHeaderComponent,
    UiSkeletonComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './purchase-order-detail.component.html',
  styleUrl: './purchase-order-detail.component.scss'
})
export class PurchaseOrderDetailPageComponent {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly pageTitle = inject(PageTitleService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(PurchaseOrdersStore);

  /** Route parameter. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly backLink = ['/', PURCHASE_ORDER_ROUTE];
  readonly statusLabels = PURCHASE_ORDER_STATUS_LABELS;
  readonly receiptStatusLabels = DOCUMENT_STATUS_LABELS;
  readonly tone: Readonly<Record<string, StatusTone>> = PO_STATUS_TONE;
  readonly receiptTone: Readonly<Record<string, StatusTone>> = STATUS_TONE;

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

  constructor() {
    this.destroyRef.onDestroy(() => this.pageTitle.setDetail(undefined));
  }

  onRetryLoad(): void {
    this.store.detail.reload();
  }
}
