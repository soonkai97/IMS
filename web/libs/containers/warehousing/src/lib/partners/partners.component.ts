import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { tap } from 'rxjs';
import { PartnerDto, PartnerType } from '@ims/api';
import { I18nService, PARTNER_TYPE_LABELS, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';
import { PartnerQuery, PartnersStore } from '@ims/features/warehousing';
import { API_ERROR_KIND, DEFAULT_PAGE_SIZE, PERMISSION } from '@ims/models';
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
  UiFormLegendComponent,
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
  SERVER_ERROR_KEY,
  applyFieldErrors,
  controlSignal,
  createPageState,
  debouncedSignal,
  loadOnChange
} from '@ims/utils';

interface PartnerFormValue {
  readonly type: PartnerType;
  readonly name: string;
  readonly contact: string;
  readonly isActive: boolean;
}

@Component({
  selector: 'app-partners-page',
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
    UiFormLegendComponent,
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
  templateUrl: './partners.component.html',
  styleUrl: './partners.component.scss'
})
export class PartnersPageComponent {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(PartnersStore);
  private readonly toast = inject(ToastService);

  readonly typeFilter = new FormControl<PartnerType | null>(null);
  readonly inactiveFilter = new FormControl(false, { nonNullable: true });
  readonly form = new FormGroup({
    type: new FormControl<PartnerType>(PartnerType.Supplier, { nonNullable: true }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)]
    }),
    contact: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(256)] }),
    isActive: new FormControl(true, { nonNullable: true })
  });

  $searchInput = signal('');
  $dialogOpen = signal(false);
  $editingId = signal<number | undefined>(undefined);

  private readonly $search = debouncedSignal(this.$searchInput);
  private readonly $type = controlSignal(this.typeFilter);
  private readonly $includeInactive = controlSignal(this.inactiveFilter);
  private readonly $filterKey = computed(() =>
    JSON.stringify([this.$search(), this.$type(), this.$includeInactive()])
  );
  private readonly pageState = createPageState(this.$filterKey);

  readonly $query = computed<PartnerQuery>(() => ({
    type: this.$type() ?? undefined,
    includeInactive: this.$includeInactive(),
    search: this.$search() || undefined,
    page: this.pageState.$page(),
    pageSize: DEFAULT_PAGE_SIZE
  }));

  $page = this.store.list.$data;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $isSaving = this.store.save.$isRunning;
  /** New: `partners.create` (a Clerk has it). Edit and deactivate: `masterdata.manage` only. */
  $canCreate = computed(() => this.permissions.has(PERMISSION.PartnersCreate));
  $canManage = computed(() => this.permissions.has(PERMISSION.MasterDataManage));
  $typeOptions = computed<readonly SelectOption[]>(() => [
    { value: PartnerType.Supplier, label: this.i18n.t('partnerType.Supplier') },
    { value: PartnerType.Customer, label: this.i18n.t('partnerType.Customer') }
  ]);
  $dialogHeading = computed(() =>
    this.i18n.t(this.$editingId() === undefined ? 'partner.new' : 'partner.edit')
  );
  $columns = computed<readonly TableColumn<PartnerDto>[]>(() => {
    const columns: TableColumn<PartnerDto>[] = [
      { key: 'name', header: this.i18n.t('partner.name'), value: row => row.name, primary: true },
      {
        key: 'type',
        header: this.i18n.t('partner.type'),
        value: row => this.i18n.enumLabel(PARTNER_TYPE_LABELS, row.type)
      },
      {
        key: 'contact',
        header: this.i18n.t('partner.contact'),
        value: row => row.contact ?? '',
        hideOnPhone: true
      },
      { key: 'status', type: 'status', header: this.i18n.t('product.status') }
    ];
    if (this.$canManage()) {
      columns.push({
        key: 'actions',
        type: 'action',
        header: this.i18n.t('common.actions'),
        align: 'end'
      });
    }
    return columns;
  });
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
      if (error.kind === API_ERROR_KIND.Conflict && error.detail) {
        // 409: a partner of this type already has this name. The server's sentence says which one.
        this.form.controls.name.setErrors({ [SERVER_ERROR_KEY]: error.detail });
        this.form.controls.name.markAsTouched();
      }
    }
  });

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  onAdd(): void {
    this.open(undefined, { type: PartnerType.Supplier, name: '', contact: '', isActive: true });
  }

  /** A supplier and a customer may share a name: the type tells their Edit buttons apart (ER-10). */
  nameOf(row: PartnerDto): string {
    return `${row.name} (${this.i18n.enumLabel(PARTNER_TYPE_LABELS, row.type)})`;
  }

  onEdit(row: PartnerDto): void {
    this.open(row.id, {
      type: row.type,
      name: row.name,
      contact: row.contact ?? '',
      isActive: row.isActive
    });
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  onRetry(): void {
    this.store.list.reload();
  }

  onSave(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    const value = this.form.getRawValue();
    this.store.save
      .run({
        id: this.$editingId(),
        body: {
          type: value.type,
          name: value.name.trim(),
          contact: value.contact.trim() || undefined,
          isActive: value.isActive
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

  private open(id: number | undefined, value: PartnerFormValue): void {
    this.store.save.reset();
    this.form.reset(value);
    this.$editingId.set(id);
    this.$dialogOpen.set(true);
  }
}
