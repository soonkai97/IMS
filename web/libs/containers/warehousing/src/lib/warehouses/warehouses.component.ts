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
import { WarehouseDto } from '@ims/api';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';
import { WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION } from '@ims/models';
import {
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
  UiStatusChipComponent,
  UiTableComponent,
  UiTextareaComponent
} from '@ims/ui';
import { applyFieldErrors } from '@ims/utils';

interface WarehouseFormValue {
  readonly code: string;
  readonly name: string;
  readonly contact: string;
  readonly notes: string;
  readonly isActive: boolean;
}

@Component({
  selector: 'app-warehouses-page',
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
    UiStatusChipComponent,
    UiTableComponent,
    UiTextareaComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './warehouses.component.html',
  styleUrl: './warehouses.component.scss'
})
export class WarehousesPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(WarehousesStore);
  private readonly toast = inject(ToastService);

  readonly form = new FormGroup({
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(16)]
    }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(128)]
    }),
    contact: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(256)] }),
    notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
    isActive: new FormControl(true, { nonNullable: true })
  });

  $dialogOpen = signal(false);
  $editingId = signal<number | undefined>(undefined);

  $rows = this.store.$warehouses;
  $isLoading = this.store.list.$isLoading;
  $failed = this.store.list.$isFailed;
  $isSaving = this.store.save.$isRunning;
  $canManage = computed(() => this.permissions.has(PERMISSION.MasterDataManage));
  $dialogHeading = computed(() =>
    this.i18n.t(this.$editingId() === undefined ? 'warehouse.new' : 'warehouse.edit')
  );
  $columns = computed<readonly TableColumn<WarehouseDto>[]>(() => {
    const columns: TableColumn<WarehouseDto>[] = [
      { key: 'code', header: this.i18n.t('warehouse.code'), value: row => row.code, primary: true },
      { key: 'name', header: this.i18n.t('warehouse.name'), value: row => row.name },
      {
        key: 'contact',
        header: this.i18n.t('warehouse.contact'),
        value: row => row.contact ?? '',
        hideOnPhone: true
      },
      { key: 'status', header: this.i18n.t('product.status') }
    ];
    if (this.$canManage()) {
      columns.push({ key: 'actions', header: this.i18n.t('common.actions'), align: 'end' });
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
    }
  });

  ngOnInit(): void {
    this.store.list.load(true);
  }

  onAdd(): void {
    this.open(undefined, { code: '', name: '', contact: '', notes: '', isActive: true });
  }

  onEdit(row: WarehouseDto): void {
    this.open(row.id, {
      code: row.code,
      name: row.name,
      contact: row.contact ?? '',
      notes: row.notes ?? '',
      isActive: row.isActive
    });
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
          code: value.code.trim(),
          name: value.name.trim(),
          contact: value.contact.trim() || undefined,
          notes: value.notes.trim() || undefined,
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

  private open(id: number | undefined, value: WarehouseFormValue): void {
    this.store.save.reset();
    this.form.reset(value);
    this.$editingId.set(id);
    this.$dialogOpen.set(true);
  }
}
