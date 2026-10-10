import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { AuditLogDto } from '@ims/api';
import { AppDatePipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { AuditQuery, AuditStore } from '@ims/features/audit';
import { DEFAULT_PAGE_SIZE } from '@ims/models';
import {
  TableColumn,
  UiButtonComponent,
  UiCellDirective,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiTableComponent
} from '@ims/ui';
import { controlSignal, createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

/** Pretty-prints a JSON snapshot; anything that is not JSON is shown as it is. */
function prettyJson(value: string | undefined): string {
  if (!value) {
    return '';
  }
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

/** Read-only audit trail (AUTH-03): who did what, with before / after values on demand. */
@Component({
  selector: 'app-audit-log-page',
  imports: [
    AppDatePipe,
    ReactiveFormsModule,
    TranslatePipe,
    UiButtonComponent,
    UiCellDirective,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-log.component.html',
  styleUrl: './audit-log.component.scss'
})
export class AuditLogPageComponent {
  private readonly i18n = inject(I18nService);
  private readonly store = inject(AuditStore);

  readonly entityControl = new FormControl('', { nonNullable: true });
  readonly actorControl = new FormControl('', { nonNullable: true });
  readonly fromControl = new FormControl('', { nonNullable: true });
  readonly toControl = new FormControl('', { nonNullable: true });

  private readonly $entity = debouncedSignal(controlSignal(this.entityControl));
  private readonly $actor = debouncedSignal(controlSignal(this.actorControl));
  private readonly $from = controlSignal(this.fromControl);
  private readonly $to = controlSignal(this.toControl);
  private readonly pageState = createPageState(
    computed(() => JSON.stringify([this.$entity(), this.$actor(), this.$from(), this.$to()]))
  );

  readonly $query = computed<AuditQuery>(() => ({
    entityType: this.$entity() || undefined,
    actor: this.$actor() || undefined,
    from: this.$from() || undefined,
    to: this.$to() || undefined,
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $detail = signal<AuditLogDto | undefined>(undefined);
  $detailOpen = signal(false);
  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $before = computed(() => prettyJson(this.$detail()?.before));
  $after = computed(() => prettyJson(this.$detail()?.after));
  $columns = computed<readonly TableColumn<AuditLogDto>[]>(() => [
    { key: 'at', header: this.i18n.t('audit.at'), primary: true },
    { key: 'actor', header: this.i18n.t('audit.actor'), value: row => row.actor ?? '' },
    { key: 'action', header: this.i18n.t('audit.action'), value: row => row.action ?? '' },
    {
      key: 'entity',
      header: this.i18n.t('audit.entity'),
      value: row => [row.entityType, row.entityId].filter(Boolean).join(' #')
    },
    { key: 'details', header: this.i18n.t('audit.details'), align: 'end' }
  ]);

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onRetry(): void {
    this.store.list.reload();
  }

  onShowDetails(row: AuditLogDto): void {
    this.$detail.set(row);
    this.$detailOpen.set(true);
  }
}
