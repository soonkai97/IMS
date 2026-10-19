import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { RoleDto } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import {
  RoleKind,
  RoleQuery,
  RolesStore,
  kindOfRole,
  sortRoles
} from '@ims/features/administration';
import { PAGE_SIZES, PERMISSION } from '@ims/models';
import {
  TableColumn,
  UiCellDirective,
  UiCheckboxComponent,
  UiEmptyStateComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { controlSignal, createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

/** The API refuses a longer search (Limits.SearchMaxLength). */
const MAX_SEARCH_LENGTH = 100;

/** The kind column: icon + words, never colour alone. */
const KIND_ICON: Record<RoleKind, 'lock' | 'shield' | 'pencil'> = {
  locked: 'lock',
  system: 'shield',
  custom: 'pencil'
};
const KIND_LABEL = {
  locked: 'roles.kind.locked',
  system: 'roles.kind.system',
  custom: 'roles.kind.custom'
} as const;

/**
 * Roles (RP01, RP02): a read-only list; a row opens the role page. The menu and the route need `roles.read`; a person without
 * `roles.manage` sees the "View only" note. Which role is locked and which is a standard one comes from the data (the API's
 * `isSystem` and the admin-core flags of the permission catalog), never from a role name.
 */
@Component({
  selector: 'app-roles-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiCellDirective,
    UiCheckboxComponent,
    UiEmptyStateComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  providers: [RolesStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './roles.component.html',
  styleUrl: './roles.component.scss'
})
export class RolesPageComponent {
  private readonly i18n = inject(I18nService);
  private readonly store = inject(RolesStore);
  private readonly permissions = inject(PermissionService);

  readonly includeInactive = new FormControl(false, { nonNullable: true });

  $searchInput = signal('');
  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $includeInactive = controlSignal(this.includeInactive);
  private readonly pageState = createPageState(
    computed(() => JSON.stringify([this.$search(), this.$includeInactive()]))
  );

  readonly $query = computed<RoleQuery>(() => ({
    search: this.$search().trim().slice(0, MAX_SEARCH_LENGTH) || undefined,
    includeInactive: this.$includeInactive()
  }));

  /** The list is shown once the permission catalog is there too: the locked marker and the counts need it. */
  $isLoading = computed(() => this.store.list.$isLoading() || this.store.catalog.$isLoading());
  $failed = computed(() => this.store.list.$isFailed() || this.store.catalog.$isFailed());
  readonly searchMaxLength = MAX_SEARCH_LENGTH;
  $canManage = computed(() => this.permissions.has(PERMISSION.RolesManage));
  $isSearching = computed(() => this.$query().search !== undefined);
  readonly pageSizes = PAGE_SIZES;
  $pageSize = computed(() => this.pageState.$pageSize());
  $page = computed(() => this.pageState.$page());
  private readonly $sorted = computed(() =>
    this.$catalog() === undefined ? [] : sortRoles(this.store.list.$data() ?? [])
  );
  $total = computed(() => this.$sorted().length);
  $rows = computed<readonly RoleDto[]>(() => {
    const start = (this.$page() - 1) * this.$pageSize();
    return this.$sorted().slice(start, start + this.$pageSize());
  });
  $catalog = this.store.catalog.$data;
  $columns = computed<readonly TableColumn<RoleDto>[]>(() => [
    { key: 'name', header: this.i18n.t('roles.name'), value: row => row.name, primary: true },
    {
      key: 'description',
      header: this.i18n.t('roles.description'),
      value: row => row.description,
      hideOnPhone: true
    },
    { key: 'kind', type: 'status', header: this.i18n.t('roles.kind'), phoneEnd: true },
    {
      key: 'permissions',
      header: this.i18n.t('roles.permissions'),
      align: 'end',
      value: row => this.permissionCount(row)
    },
    {
      key: 'users',
      header: this.i18n.t('roles.users'),
      align: 'end',
      value: row => String(row.userCount)
    },
    { key: 'status', type: 'status', header: this.i18n.t('product.status'), phoneInline: true }
  ]);

  readonly rowLink = (row: RoleDto): readonly unknown[] => ['/roles', row.id];

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
    this.store.catalog.load();
  }

  kindOf(role: RoleDto): RoleKind {
    return kindOfRole(role, this.$catalog());
  }

  kindIcon(role: RoleDto): 'lock' | 'shield' | 'pencil' {
    return KIND_ICON[this.kindOf(role)];
  }

  kindLabel(role: RoleDto): string {
    return this.i18n.t(KIND_LABEL[this.kindOf(role)]);
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageState.setPageSize(pageSize);
  }

  onRetry(): void {
    this.store.list.reload();
    this.store.catalog.reload();
  }

  private permissionCount(role: RoleDto): string {
    return this.i18n.t('roles.permissionsOf', {
      count: role.permissions.length,
      total: this.$catalog()?.length ?? 0
    });
  }
}
