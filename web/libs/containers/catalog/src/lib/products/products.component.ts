import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ProductDto, ProductSort } from '@ims/api';
import { AppQtyPipe, TranslatePipe, I18nService } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { CatalogStore, ProductQuery, ProductsStore } from '@ims/features/catalog';
import { PAGE_SIZES, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  TableColumn,
  TableSort,
  UiCellDirective,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiSelectComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { controlSignal, createPageState, debouncedSignal, loadOnChange } from '@ims/utils';

const STATUS_ALL = 'all';
const STATUS_ACTIVE = 'active';
const STATUS_INACTIVE = 'inactive';
type ActiveFilter = typeof STATUS_ALL | typeof STATUS_ACTIVE | typeof STATUS_INACTIVE;

const SORT_KEYS: Readonly<Record<string, ProductSort>> = {
  Sku: ProductSort.Sku,
  Name: ProductSort.Name
};

@Component({
  selector: 'app-products-page',
  imports: [
    AppQtyPipe,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiCellDirective,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiSelectComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './products.component.html',
  styleUrl: './products.component.scss'
})
export class ProductsPageComponent implements OnInit {
  private readonly catalog = inject(CatalogStore);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(ProductsStore);

  readonly categoryControl = new FormControl<PublicId | null>(null);
  readonly statusControl = new FormControl<ActiveFilter>(STATUS_ACTIVE, { nonNullable: true });

  $searchInput = signal('');
  $sort = signal<TableSort | undefined>(undefined);

  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $categoryId = controlSignal(this.categoryControl);
  private readonly $status = controlSignal(this.statusControl);
  private readonly $filterKey = computed(() =>
    JSON.stringify([this.$search(), this.$categoryId(), this.$status(), this.$sort()])
  );
  readonly pageSizes = PAGE_SIZES;
  private readonly pageState = createPageState(this.$filterKey);

  readonly $query = computed<ProductQuery>(() => ({
    search: this.$search() || undefined,
    categoryId: this.$categoryId() ?? undefined,
    active: this.activeFlag(this.$status()),
    page: this.pageState.$page(),
    pageSize: this.pageState.$pageSize(),
    sortBy: SORT_KEYS[this.$sort()?.sortBy ?? ''],
    desc: this.$sort()?.desc
  }));

  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $canManage = computed(() => this.permissions.has(PERMISSION.MasterDataManage));
  $hasFilters = computed(
    () => this.$filterKey() !== JSON.stringify(['', null, STATUS_ACTIVE, undefined])
  );

  $categoryOptions = computed<readonly SelectOption[]>(() =>
    this.catalog.$categories().map(category => ({ value: category.id, label: category.name }))
  );
  $statusOptions = computed<readonly SelectOption[]>(() => [
    { value: STATUS_ALL, label: this.i18n.t('filter.all') },
    { value: STATUS_ACTIVE, label: this.i18n.t('product.active') },
    { value: STATUS_INACTIVE, label: this.i18n.t('product.inactive') }
  ]);
  $columns = computed<readonly TableColumn<ProductDto>[]>(() => [
    {
      key: 'sku',
      type: 'code',
      header: this.i18n.t('product.sku'),
      value: row => row.sku,
      primary: true,
      sortKey: ProductSort.Sku
    },
    {
      key: 'name',
      header: this.i18n.t('product.name'),
      value: row => row.name,
      phoneInline: true,
      sortKey: ProductSort.Name
    },
    {
      key: 'category',
      header: this.i18n.t('product.category'),
      value: row => row.categoryName ?? '',
      hideOnPhone: true
    },
    { key: 'unit', header: this.i18n.t('product.baseUnit'), value: row => row.baseUomCode },
    { key: 'safety', header: this.i18n.t('product.safetyStock'), align: 'end', hideOnPhone: true },
    { key: 'active', header: this.i18n.t('product.status'), phoneEnd: true }
  ]);

  readonly rowLink = (row: ProductDto): readonly unknown[] => ['/products', row.id];

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  ngOnInit(): void {
    this.catalog.ensureLoaded();
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onPageSizeChange(pageSize: number): void {
    this.pageState.setPageSize(pageSize);
  }

  onSortChange(sort: TableSort): void {
    this.$sort.set(sort);
  }

  onClearFilters(): void {
    this.$searchInput.set('');
    this.categoryControl.setValue(null);
    this.statusControl.setValue(STATUS_ACTIVE);
    this.$sort.set(undefined);
  }

  onRetry(): void {
    this.store.list.reload();
  }

  private activeFlag(status: ActiveFilter): boolean | undefined {
    if (status === STATUS_ALL) {
      return undefined;
    }
    return status === STATUS_ACTIVE;
  }
}
