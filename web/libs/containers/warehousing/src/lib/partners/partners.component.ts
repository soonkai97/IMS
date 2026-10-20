import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { tap } from 'rxjs';
import { PartnerDto, PartnerType } from '@ims/api';
import { I18nService, PARTNER_TYPE_LABELS, TranslatePipe } from '@ims/core/i18n';
import { ModulesStore, PermissionService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';
import { PartnerQuery, PartnersStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  DEFAULT_PAGE_SIZE,
  MODULE_CODE,
  PERMISSION,
  PublicId,
  TAX_FIELD
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
  cleanTaxField,
  controlSignal,
  createPageState,
  debouncedSignal,
  focusFirstInvalid,
  loadOnChange,
  taxFieldShape
} from '@ims/utils';

interface PartnerFormValue {
  readonly type: PartnerType;
  readonly name: string;
  readonly contact: string;
  readonly isActive: boolean;
  /** Module accounting; empty when the partner has none or the module is off. */
  readonly tin: string;
  readonly sstRegNo: string;
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly i18n = inject(I18nService);
  private readonly modules = inject(ModulesStore);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(PartnersStore);
  private readonly toast = inject(ToastService);

  readonly tax = TAX_FIELD;
  readonly typeFilter = new FormControl<PartnerType | null>(null);
  readonly inactiveFilter = new FormControl(false, { nonNullable: true });
  readonly form = new FormGroup({
    type: new FormControl<PartnerType>(PartnerType.Supplier, { nonNullable: true }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)]
    }),
    contact: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(256)] }),
    isActive: new FormControl(true, { nonNullable: true }),
    // Module accounting. Shown and sent only while $taxFields() says so. Nothing is cut while typing: the server trims, then checks.
    tin: new FormControl('', { nonNullable: true, validators: [taxFieldShape(TAX_FIELD.Tin)] }),
    sstRegNo: new FormControl('', {
      nonNullable: true,
      validators: [taxFieldShape(TAX_FIELD.SstRegNo)]
    })
  });

  $searchInput = signal('');
  $dialogOpen = signal(false);
  $editingId = signal<PublicId | undefined>(undefined);
  /** The partner being edited came with a tax identifier: the server had the module on when it answered (it leaves them out while off). */
  private readonly $openedWithTaxIds = signal(false);
  /**
   * The two tax identifiers are shown and ALWAYS sent while the module is on (a save replaces them, so a form that did not send them
   * would clear them). A screen whose module list is older than the row it shows (another tab switched the module on) still keeps
   * the identifiers the server just showed: one on the row proves the module was on.
   */
  $taxFields = computed(
    () => this.modules.isEnabled(MODULE_CODE.Accounting) || this.$openedWithTaxIds()
  );
  /**
   * The module was on when this partner was opened (the list said so, or the row carried a tax identifier). A row read while it was
   * OFF came without its identifiers (the server leaves them out), so saving it after the module was turned on would send empty ones
   * and clear the stored ones: that save is refused and the partner is read again instead.
   */
  private readonly $openedWhileOn = signal(false);
  /** The save was refused because the module was turned on after the partner was read; it was read again and reopened. */
  $taxIdsToCheck = signal(false);
  /** The partner to open again once the list has been read again. */
  private readonly $reopenId = signal<PublicId | undefined>(undefined);

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

  private readonly reopenAfterReload = effect(() => {
    const id = this.$reopenId();
    if (id === undefined || this.store.list.$isLoading()) {
      return;
    }
    const row = this.store.list.$data()?.items.find(item => item.id === id);
    untracked(() => {
      this.$reopenId.set(undefined);
      if (row) {
        this.open(row.id, this.formValueOf(row));
        this.$taxIdsToCheck.set(true);
      } else {
        this.$dialogOpen.set(false);
      }
    });
  });

  private readonly showServerErrors = effect(() => {
    const error = this.store.save.$operation().error;
    if (error) {
      applyFieldErrors(this.form, error);
      if (error.kind === API_ERROR_KIND.Conflict) {
        // 409: a partner of this type already has this name. The server's sentence says which one.
        const sentence = error.detail || (error.message ?? this.i18n.t('error.generic'));
        this.form.controls.name.setErrors({ [SERVER_ERROR_KEY]: sentence });
        this.form.controls.name.markAsTouched();
      }
    }
  });

  constructor() {
    loadOnChange(this.$query, query => this.store.list.load(query));
  }

  onAdd(): void {
    this.open(undefined, {
      type: PartnerType.Supplier,
      name: '',
      contact: '',
      isActive: true,
      tin: '',
      sstRegNo: ''
    });
  }

  /** A supplier and a customer may share a name: the type tells their Edit buttons apart (ER-10). */
  nameOf(row: PartnerDto): string {
    return `${row.name} (${this.i18n.enumLabel(PARTNER_TYPE_LABELS, row.type)})`;
  }

  onEdit(row: PartnerDto): void {
    this.open(row.id, this.formValueOf(row));
  }

  private formValueOf(row: PartnerDto): PartnerFormValue {
    return {
      type: row.type,
      name: row.name,
      contact: row.contact ?? '',
      isActive: row.isActive,
      tin: row.tin ?? '',
      sstRegNo: row.sstRegNo ?? ''
    };
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
      // The focus goes to the first field to fix, not to stay on Save (WCAG 3.3.1).
      afterNextRender(() => focusFirstInvalid(this.host.nativeElement), {
        injector: this.injector
      });
      return;
    }
    const editing = this.$editingId();
    if (editing !== undefined && this.$taxFields() && !this.$openedWhileOn()) {
      // Accounting was turned on after this partner was read: nothing is sent; the list is read again and the partner reopened.
      this.$reopenId.set(editing);
      this.store.list.reload();
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
          isActive: value.isActive,
          // The loaded value goes back as it was; an emptied field goes as '' and clears (the server stores a blank as none).
          ...(this.$taxFields()
            ? { tin: cleanTaxField(value.tin), sstRegNo: cleanTaxField(value.sstRegNo) }
            : {})
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

  private open(id: PublicId | undefined, value: PartnerFormValue): void {
    this.store.save.reset();
    this.form.reset(value);
    this.$openedWithTaxIds.set(Boolean(value.tin || value.sstRegNo));
    this.$openedWhileOn.set(
      this.modules.isEnabled(MODULE_CODE.Accounting) || this.$openedWithTaxIds()
    );
    this.$taxIdsToCheck.set(false);
    this.$editingId.set(id);
    this.$dialogOpen.set(true);
  }
}
