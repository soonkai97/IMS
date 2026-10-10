import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { DocumentListItem } from '@ims/api';
import {
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_TYPE_LABELS,
  I18nService,
  TranslatePipe
} from '@ims/core/i18n';
import { ApprovalsStore } from '@ims/features/approvals';
import { DOCUMENT_ROUTE } from '@ims/features/documents';

import {
  TabItem,
  TableColumn,
  UiCellDirective,
  UiEmptyStateComponent,
  UiPageHeaderComponent,
  UiStatusChipComponent,
  UiTableComponent,
  UiTabsComponent
} from '@ims/ui';

const TAB_WAITING = 'waiting';
const TAB_MINE = 'mine';
const MINUTE_MS = 60_000;
const HOUR_MINUTES = 60;
const DAY_HOURS = 24;

interface InboxRow {
  readonly document: DocumentListItem;
  readonly waiting: string;
}

/** Documents waiting for approval, oldest first. Approving happens on the document itself, after reading its lines. */
@Component({
  selector: 'app-approvals-inbox-page',
  imports: [
    AppMoneyPipe,
    AppQtyPipe,
    TranslatePipe,
    UiCellDirective,
    UiEmptyStateComponent,
    UiPageHeaderComponent,
    UiStatusChipComponent,
    UiTableComponent,
    UiTabsComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox.component.html',
  styleUrl: './inbox.component.scss'
})
export class ApprovalsInboxPageComponent implements OnInit {
  private readonly i18n = inject(I18nService);
  private readonly store = inject(ApprovalsStore);

  $tab = signal<string>(TAB_WAITING);

  $isLoading = this.store.submitted.$isLoading;
  $failed = this.store.submitted.$isFailed;
  $isWaitingTab = computed(() => this.$tab() === TAB_WAITING);
  $tabs = computed<readonly TabItem[]>(() => [
    {
      id: TAB_WAITING,
      label: this.i18n.t('approvals.waitingForMe'),
      badge: this.store.$waitingForMe().length
    },
    {
      id: TAB_MINE,
      label: this.i18n.t('approvals.submittedByMe'),
      badge: this.store.$submittedByMe().length
    }
  ]);
  $rows = computed<readonly InboxRow[]>(() => {
    const documents = this.$isWaitingTab()
      ? this.store.$waitingForMe()
      : this.store.$submittedByMe();
    return documents.map(document => ({
      document,
      waiting: this.waitingText(document.submittedAt)
    }));
  });
  $columns = computed<readonly TableColumn<InboxRow>[]>(() => [
    { key: 'no', header: this.i18n.t('doc.no'), value: row => row.document.no, primary: true },
    {
      key: 'type',
      header: this.i18n.t('doc.type'),
      value: row => this.i18n.enumLabel(DOCUMENT_TYPE_LABELS, row.document.type)
    },
    {
      key: 'by',
      header: this.i18n.t('approvals.submittedBy'),
      value: row => row.document.submittedByName ?? row.document.submittedBy ?? ''
    },
    {
      key: 'warehouse',
      header: this.i18n.t('doc.warehouse'),
      value: row => row.document.warehouseName,
      hideOnPhone: true
    },
    { key: 'flags', header: this.i18n.t('approvals.flags') },
    { key: 'qty', header: this.i18n.t('doc.totalQty'), align: 'end' },
    { key: 'amount', header: this.i18n.t('doc.amount'), align: 'end' },
    { key: 'waiting', header: this.i18n.t('approvals.waiting'), value: row => row.waiting }
  ]);

  readonly rowLink = (row: InboxRow): readonly unknown[] => [
    '/',
    DOCUMENT_ROUTE[row.document.type],
    row.document.id
  ];

  ngOnInit(): void {
    this.store.refresh();
  }

  onTabChange(tab: string): void {
    this.$tab.set(tab);
  }

  onRetry(): void {
    this.store.refresh();
  }

  private waitingText(submittedAt: string | undefined): string {
    if (!submittedAt) {
      return '';
    }
    const minutes = Math.max(
      0,
      Math.floor((Date.now() - new Date(submittedAt).getTime()) / MINUTE_MS)
    );
    const hours = Math.floor(minutes / HOUR_MINUTES);
    if (hours >= DAY_HOURS) {
      return this.i18n.t('approvals.waitingDays', { count: Math.floor(hours / DAY_HOURS) });
    }
    if (hours >= 1) {
      return this.i18n.t('approvals.waitingHours', { count: hours });
    }
    return this.i18n.t('approvals.waitingMinutes', { count: minutes });
  }
}
