import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DocumentListItem, DocumentType } from '@ims/api';
import {
  AppDatePipe,
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_TYPE_LABELS,
  EnumLabelPipe,
  TranslatePipe
} from '@ims/core/i18n';
import { handledInline } from '@ims/core/http';
import { CanDirective, PermissionService } from '@ims/core/identity';
import { DOCUMENT_ROUTE, DocumentsStore } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PERMISSION } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiEmptyStateComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiSkeletonComponent,
  UiStatCardComponent,
  UiStatusChipComponent
} from '@ims/ui';
import { HomeFeedStore } from './home-feed.store';

/** Rows of "Returned to me" before "See all": a phone shows the first screenful, the list has the rest. */
const RETURNED_ROWS = 5;

/** Role-shaped start page: the numbers and shortcuts the role can act on (docs/design/mvp-ux.md 1.2). Every number opens its list. */
@Component({
  selector: 'app-home-page',
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
    UiEmptyStateComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiSkeletonComponent,
    UiStatCardComponent,
    UiStatusChipComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomePageComponent implements OnInit {
  private readonly documents = inject(DocumentsStore);
  private readonly feed = inject(HomeFeedStore);
  private readonly permissions = inject(PermissionService);
  private readonly stock = inject(StockStore);

  readonly typeLabels = DOCUMENT_TYPE_LABELS;

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
  $canApprove = computed(() =>
    this.permissions.hasAny([PERMISSION.DocumentApprove, PERMISSION.DocumentApproveLarge])
  );
  /** "Posted today" is for the roles that approve AND post (Supervisor, Admin); the Owner only reviews. */
  $canSeePostedToday = computed(
    () => this.$canApprove() && this.permissions.has(PERMISSION.DocumentPost)
  );
  /** A warehouse user without a warehouse can do nothing yet: say so instead of showing empty numbers. */
  $hasNoWarehouse = computed(
    () => !this.permissions.$seesAllWarehouses() && this.permissions.$warehouseIds().length === 0
  );

  /** Newest drafts of the user, returned ones first in the list below. */
  private readonly $drafts = computed(() => this.feed.drafts.$data()?.items ?? []);
  readonly $returned = computed(() => this.$drafts().filter(row => row.isReturned));
  $returnedRows = computed(() => this.$returned().slice(0, RETURNED_ROWS));
  $feedLoading = computed(() => this.feed.drafts.$isLoading() && !this.feed.drafts.$data());
  $feedFailed = this.feed.drafts.$isFailed;
  $feedError = computed(() => this.feed.drafts.$operation().error?.message);

  /** Receipts or issues by the first row's type; drafts of both kinds share one tile, so the first one decides (no filter links yet). */
  $returnedLink = computed(() => this.listLink(this.$returned()));
  $draftsLink = computed(() => this.listLink(this.$drafts()));
  /** The posted documents of both types are one ledger of movements; a user without reports.read gets the receipts list. */
  $postedTodayLink = computed(() =>
    this.permissions.has(PERMISSION.ReportsRead) ? ['/reports'] : ['/receipts']
  );

  ngOnInit(): void {
    if (this.$canViewStock()) {
      this.stock.summary.load(undefined, handledInline());
      this.documents.counts.load(undefined, handledInline());
      if (this.$canCreate()) {
        this.feed.drafts.load(undefined, handledInline());
      }
    }
  }

  onRetry(): void {
    this.stock.summary.reload();
    this.documents.counts.reload();
    if (this.$canCreate()) {
      this.feed.drafts.reload();
    }
  }

  onRetryFeed(): void {
    this.feed.drafts.reload();
  }

  documentLink(row: DocumentListItem): string[] {
    return ['/', DOCUMENT_ROUTE[row.type], row.id];
  }

  isIssue(row: DocumentListItem): boolean {
    return row.type === DocumentType.Issue;
  }

  private listLink(rows: readonly DocumentListItem[]): string[] {
    return ['/', DOCUMENT_ROUTE[rows[0]?.type ?? DocumentType.Receipt]];
  }
}
