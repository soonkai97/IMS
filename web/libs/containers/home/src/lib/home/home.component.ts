import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApprovalsStore } from '@ims/features/approvals';
import { AppMoneyPipe, AppQtyPipe, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { StockStore } from '@ims/features/stock';
import { PERMISSION } from '@ims/models';
import {
  UiBannerComponent,
  UiEmptyStateComponent,
  UiPageHeaderComponent,
  UiSkeletonComponent,
  UiStatCardComponent
} from '@ims/ui';

/** Role-shaped start page: the numbers and shortcuts the role can act on (docs/design/mvp-ux.md 1.2). */
@Component({
  selector: 'app-home-page',
  imports: [
    AppMoneyPipe,
    AppQtyPipe,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiEmptyStateComponent,
    UiPageHeaderComponent,
    UiSkeletonComponent,
    UiStatCardComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomePageComponent implements OnInit {
  private readonly approvals = inject(ApprovalsStore);
  private readonly permissions = inject(PermissionService);
  private readonly stock = inject(StockStore);

  $summary = this.stock.summary.$data;
  $isLoading = this.stock.summary.$isLoading;
  $failed = this.stock.summary.$isFailed;
  $waitingApprovals = this.approvals.$waitingCount;
  $displayName = this.permissions.$displayName;

  $canViewStock = computed(() => this.permissions.has(PERMISSION.StockRead));
  $canCreate = computed(() => this.permissions.has(PERMISSION.DocumentCreate));
  $canApprove = computed(() => this.permissions.has(PERMISSION.DocumentApprove));
  /** A warehouse user without a warehouse can do nothing yet: say so instead of showing empty numbers. */
  $hasNoWarehouse = computed(
    () => !this.permissions.$seesAllWarehouses() && this.permissions.$warehouseIds().length === 0
  );

  ngOnInit(): void {
    if (this.$canViewStock()) {
      this.stock.summary.load();
    }
    if (this.$canApprove()) {
      this.approvals.refresh();
    }
  }

  onRetry(): void {
    this.stock.summary.reload();
  }
}
