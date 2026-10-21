import { DOCUMENT } from '@angular/common';
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
import { handledInline } from '@ims/core/http';
import { AppDatePipe, AppMoneyPipe, AppQtyPipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { PageTitleService } from '@ims/core/navigation';
import { PURCHASE_ORDER_ROUTE, PurchaseOrdersStore } from '@ims/features/purchasing';
import { API_ERROR_KIND } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiEmptyStateComponent,
  UiSkeletonComponent
} from '@ims/ui';
import { normalizePublicId } from '@ims/utils';

/** Marks the page while it is open, so the print stylesheet of the app can hide the navigation around it. */
const PRINT_VIEW_CLASS = 'po-print-view';

/**
 * The supplier copy of one purchase order: number, date, supplier, ship-to warehouse, lines with quantity, unit, price and amount,
 * the total and the remark. NOTHING internal: no history, no reasons, no people. Prices come from the API only for a caller with
 * `cost.view` (the route asks for it), and a missing one is an empty cell, never 0.00. A4, light colours, no navigation (the app's
 * print stylesheet), a Print button that is not printed.
 */
@Component({
  selector: 'app-purchase-order-print-page',
  imports: [
    AppDatePipe,
    AppMoneyPipe,
    AppQtyPipe,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiEmptyStateComponent,
    UiSkeletonComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './purchase-order-print.component.html',
  styleUrl: './purchase-order-print.component.scss'
})
export class PurchaseOrderPrintPageComponent {
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly i18n = inject(I18nService);
  private readonly pageTitle = inject(PageTitleService);
  private readonly store = inject(PurchaseOrdersStore);

  /** Route parameter. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  private readonly $orderId = computed(() => normalizePublicId(this.$id()));
  readonly $order = computed(() => {
    const order = this.store.detail.$data();
    return order && order.id === this.$orderId() ? order : undefined;
  });
  readonly $isLoading = computed(
    () => this.store.detail.$isLoading() && this.$order() === undefined
  );
  /** Not a GUID (an old bookmark): no request is made at all. */
  readonly $notFound = computed(
    () =>
      (this.$id() !== undefined && this.$orderId() === undefined) ||
      this.store.detail.$operation().error?.kind === API_ERROR_KIND.NotFound
  );
  readonly $failed = computed(() => this.store.detail.$isFailed() && !this.$notFound());
  readonly $errorMessage = computed(() => this.store.detail.$operation().error?.message);
  readonly backLink = computed(() => ['/', PURCHASE_ORDER_ROUTE, this.$orderId() ?? '']);

  private readonly openOrder = effect(() => {
    const id = this.$orderId();
    untracked(() => {
      this.store.detail.reset();
      if (id !== undefined) {
        this.store.detail.load(id, handledInline());
      }
    });
  });

  private readonly syncTitle = effect(() =>
    this.pageTitle.setDetail(this.$order()?.no ?? this.i18n.t('po.print'))
  );

  constructor() {
    this.document.body.classList.add(PRINT_VIEW_CLASS);
    this.destroyRef.onDestroy(() => {
      this.document.body.classList.remove(PRINT_VIEW_CLASS);
      this.pageTitle.setDetail(undefined);
    });
  }

  onPrint(): void {
    this.document.defaultView?.print();
  }

  onRetry(): void {
    this.store.detail.reload();
  }
}
