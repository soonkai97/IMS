import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppMoneyPipe, AppQtyPipe, TranslatePipe } from '@ims/core/i18n';
import { handledInline } from '@ims/core/http';
import { CanDirective, PermissionService } from '@ims/core/identity';
import { DocumentsStore } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PERMISSION } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
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
    CanDirective,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
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
  private readonly documents = inject(DocumentsStore);
  private readonly permissions = inject(PermissionService);
  private readonly stock = inject(StockStore);

  $summary = this.stock.summary.$data;
  $counts = this.documents.counts.$data;
  $isLoading = this.stock.summary.$isLoading;
  /** Both loads are flagged handledInline: the banner is the one announcement of either failing, with the mapped sentence. */
  $failed = computed(() => this.stock.summary.$isFailed() || this.documents.counts.$isFailed());
  $errorMessage = computed(
    () =>
      this.stock.summary.$operation().error?.message ??
      this.documents.counts.$operation().error?.message
  );
  $displayName = this.permissions.$displayName;

  $canViewStock = computed(() => this.permissions.has(PERMISSION.StockRead));
  $canCreate = computed(() => this.permissions.has(PERMISSION.DocumentCreate));
  $canApprove = computed(() => this.permissions.has(PERMISSION.DocumentApprove));
  /** "Posted today" is for the roles that approve AND post (Supervisor, Admin); the Owner only reviews. */
  $canSeePostedToday = computed(
    () => this.$canApprove() && this.permissions.has(PERMISSION.DocumentPost)
  );
  /** A warehouse user without a warehouse can do nothing yet: say so instead of showing empty numbers. */
  $hasNoWarehouse = computed(
    () => !this.permissions.$seesAllWarehouses() && this.permissions.$warehouseIds().length === 0
  );

  ngOnInit(): void {
    if (this.$canViewStock()) {
      this.stock.summary.load(undefined, handledInline());
      this.documents.counts.load(undefined, handledInline());
    }
  }

  onRetry(): void {
    this.stock.summary.reload();
    this.documents.counts.reload();
  }
}
