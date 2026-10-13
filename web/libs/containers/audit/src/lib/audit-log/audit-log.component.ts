import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { AuditLogDto } from '@ims/api';
import { AppDatePipe, I18nService, TranslatePipe } from '@ims/core/i18n';
import { AuditQuery, AuditStore } from '@ims/features/audit';
import { DEFAULT_PAGE_SIZE } from '@ims/models';
import {
  SelectOption,
  TableColumn,
  UiButtonComponent,
  UiCellDirective,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiSelectComponent,
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
    UiSearchInputComponent,
    UiSelectComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-log.component.html',
  styleUrl: './audit-log.component.scss'
})
export class AuditLogPageComponent implements OnInit {
  private readonly i18n = inject(I18nService);
  private readonly store = inject(AuditStore);

  readonly actionControl = new FormControl<string | null>(null);
  readonly actorControl = new FormControl('', { nonNullable: true });
  readonly fromControl = new FormControl('', { nonNullable: true });
  readonly toControl = new FormControl('', { nonNullable: true });

  $searchInput = signal('');
  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $action = controlSignal(this.actionControl);
  private readonly $actor = debouncedSignal(controlSignal(this.actorControl));
  private readonly $from = controlSignal(this.fromControl);
  private readonly $to = controlSignal(this.toControl);
  private readonly pageState = createPageState(
    computed(() =>
      JSON.stringify([this.$search(), this.$action(), this.$actor(), this.$from(), this.$to()])
    )
  );

  readonly $query = computed<AuditQuery>(() => ({
    search: this.$search() || undefined,
    action: this.$action() || undefined,
    actor: this.$actor() || undefined,
    from: this.$from() || undefined,
    to: this.$to() || undefined,
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  /** What the API knows how to filter by: a dropdown instead of guessing technical names. */
  $actionOptions = computed<readonly SelectOption[]>(() =>
    (this.store.filters.$data()?.actions ?? []).map(action => ({ value: action, label: action }))
  );
  $hasFilters = computed(
    () =>
      this.$search() !== '' ||
      this.$action() !== null ||
      this.$actor() !== '' ||
      this.$from() !== '' ||
      this.$to() !== ''
  );
  $detail = signal<AuditLogDto | undefined>(undefined);
  $detailOpen = signal(false);
  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $before = computed(() => prettyJson(this.$detail()?.before));
  $after = computed(() => prettyJson(this.$detail()?.after));
  $columns = computed<readonly TableColumn<AuditLogDto>[]>(() => [
    { key: 'at', type: 'date', header: this.i18n.t('audit.at'), primary: true },
    { key: 'actor', header: this.i18n.t('audit.actor'), value: row => row.actor ?? '' },
    { key: 'action', header: this.i18n.t('audit.action'), value: row => row.action ?? '' },
    {
      key: 'entity',
      header: this.i18n.t('audit.entity'),
      value: row => this.entityOf(row)
    },
    { key: 'details', header: this.i18n.t('audit.details'), align: 'end' }
  ]);

  /** "Product #12": what the entry is about. */
  entityOf(row: AuditLogDto): string {
    return [row.entityType, row.entityId].filter(Boolean).join(' #');
  }

  /** The place of the row in the whole list (1 = newest): tells apart two entries that read the same. A position, not an id: the API gives audit entries none. */
  entryNumber(row: AuditLogDto): number {
    const page = this.$page();
    return page ? (page.page - 1) * page.pageSize + page.items.indexOf(row) + 1 : 0;
  }

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  ngOnInit(): void {
    this.store.filters.load();
  }

  onClearFilters(): void {
    this.$searchInput.set('');
    this.actionControl.setValue(null);
    this.actorControl.setValue('');
    this.fromControl.setValue('');
    this.toControl.setValue('');
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
