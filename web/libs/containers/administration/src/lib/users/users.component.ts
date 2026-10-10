import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { tap } from 'rxjs';
import { UserDto } from '@ims/api';
import { I18nService, ROLE_LABELS, TranslatePipe } from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import { UserQuery, UsersStore } from '@ims/features/administration';
import { WarehousesStore } from '@ims/features/warehousing';
import {
  ALL_ROLES,
  DEFAULT_PAGE_SIZE,
  MAX_PASSWORD_LENGTH,
  MAX_USERNAME_LENGTH
} from '@ims/models';
import {
  SelectOption,
  TableColumn,
  UiBannerComponent,
  UiButtonComponent,
  UiCellDirective,
  UiCheckboxComponent,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiSearchInputComponent,
  UiSelectComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import {
  applyFieldErrors,
  controlSignal,
  createPageState,
  debouncedSignal,
  loadOnChange
} from '@ims/utils';

interface WarehouseChoice {
  readonly id: number;
  readonly label: string;
  readonly control: FormControl<boolean>;
}

/** Users and roles (admin). Passwords are write-only; leaving the field empty on edit keeps the current one. */
@Component({
  selector: 'app-users-page',
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCellDirective,
    UiCheckboxComponent,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiSearchInputComponent,
    UiSelectComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './users.component.html',
  styleUrl: './users.component.scss'
})
export class UsersPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly store = inject(UsersStore);
  private readonly toast = inject(ToastService);
  private readonly warehouses = inject(WarehousesStore);

  readonly roleFilter = new FormControl<string | null>(null);
  readonly maxUsernameLength = MAX_USERNAME_LENGTH;
  readonly maxPasswordLength = MAX_PASSWORD_LENGTH;
  readonly form = new FormGroup({
    username: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_USERNAME_LENGTH)]
    }),
    displayName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)]
    }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(256)]
    }),
    role: new FormControl<string | null>(null, [Validators.required]),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_PASSWORD_LENGTH)]
    }),
    isActive: new FormControl(true, { nonNullable: true })
  });

  $searchInput = signal('');
  $dialogOpen = signal(false);
  $editingId = signal<number | undefined>(undefined);
  $warehouseChoices = signal<readonly WarehouseChoice[]>([]);

  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $role = controlSignal(this.roleFilter);
  private readonly pageState = createPageState(
    computed(() => JSON.stringify([this.$search(), this.$role()]))
  );

  readonly $query = computed<UserQuery>(() => ({
    search: this.$search() || undefined,
    role: this.$role() ?? undefined,
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $isSaving = this.store.save.$isRunning;
  $isEditing = computed(() => this.$editingId() !== undefined);
  $dialogHeading = computed(() => this.i18n.t(this.$isEditing() ? 'user.edit' : 'user.new'));
  $roleOptions = computed<readonly SelectOption[]>(() =>
    ALL_ROLES.map(role => ({ value: role, label: this.i18n.enumLabel(ROLE_LABELS, role) }))
  );
  $columns = computed<readonly TableColumn<UserDto>[]>(() => [
    {
      key: 'username',
      header: this.i18n.t('user.username'),
      value: row => row.username,
      primary: true
    },
    { key: 'name', header: this.i18n.t('user.displayName'), value: row => row.displayName },
    { key: 'email', header: this.i18n.t('user.email'), value: row => row.email, hideOnPhone: true },
    {
      key: 'role',
      header: this.i18n.t('user.role'),
      value: row => this.i18n.enumLabel(ROLE_LABELS, row.role)
    },
    { key: 'status', header: this.i18n.t('product.status') },
    { key: 'actions', header: this.i18n.t('common.actions'), align: 'end' }
  ]);
  $serverErrors = computed(() => {
    const error = this.store.save.$operation().error;
    if (!error) {
      return [];
    }
    return Object.entries(error.fieldErrors)
      .filter(([field]) => !this.form.get(field))
      .flatMap(([, messages]) => messages);
  });

  private readonly showServerErrors = effect(() => {
    const error = this.store.save.$operation().error;
    if (error) {
      applyFieldErrors(this.form, error);
    }
  });

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  ngOnInit(): void {
    this.warehouses.ensureLoaded();
  }

  onAdd(): void {
    this.open(undefined, undefined);
  }

  onEdit(row: UserDto): void {
    this.open(row.id, row);
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onRetry(): void {
    this.store.list.reload();
  }

  onSave(): void {
    const isNew = !this.$isEditing();
    this.form.markAllAsTouched();
    const passwordMissing = isNew && this.form.controls.password.value.length === 0;
    if (passwordMissing) {
      this.form.controls.password.setErrors({ required: true });
    }
    if (this.form.invalid) {
      return;
    }
    const value = this.form.getRawValue();
    this.store.save
      .run({
        id: this.$editingId(),
        body: {
          username: value.username.trim(),
          displayName: value.displayName.trim(),
          email: value.email.trim(),
          role: value.role ?? '',
          isActive: value.isActive,
          password: value.password.length > 0 ? value.password : undefined,
          warehouseIds: this.$warehouseChoices()
            .filter(choice => choice.control.value)
            .map(choice => choice.id)
        }
      })
      .pipe(
        tap(() => {
          this.$dialogOpen.set(false);
          this.toast.success(this.i18n.t('common.saved'));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private open(id: number | undefined, user: UserDto | undefined): void {
    this.store.save.reset();
    const selected = new Set(user?.warehouseIds ?? []);
    this.$warehouseChoices.set(
      this.warehouses.$activeWarehouses().map(warehouse => ({
        id: warehouse.id,
        label: `${warehouse.code} - ${warehouse.name}`,
        control: new FormControl(selected.has(warehouse.id), { nonNullable: true })
      }))
    );
    this.form.reset({
      username: user?.username ?? '',
      displayName: user?.displayName ?? '',
      email: user?.email ?? '',
      role: user?.role ?? null,
      password: '',
      isActive: user?.isActive ?? true
    });
    this.$editingId.set(id);
    this.$dialogOpen.set(true);
  }
}
