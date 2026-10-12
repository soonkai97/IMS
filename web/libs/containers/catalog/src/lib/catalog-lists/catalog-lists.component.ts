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
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { ToastService } from '@ims/core/notification';
import { CatalogStore } from '@ims/features/catalog';
import { PERMISSION } from '@ims/models';
import {
  TabItem,
  TableColumn,
  UiBannerComponent,
  UiButtonComponent,
  UiCellDirective,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiTableComponent,
  UiTabsComponent
} from '@ims/ui';
import { applyFieldErrors } from '@ims/utils';

const TAB_CATEGORIES = 'categories';
const TAB_UNITS = 'units';

interface ListRow {
  readonly id: number;
  readonly code: string;
  readonly name: string;
}

/** Categories and units of measure: two small lists edited in a dialog. */
@Component({
  selector: 'app-catalog-lists-page',
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCellDirective,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiTableComponent,
    UiTabsComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalog-lists.component.html',
  styleUrl: './catalog-lists.component.scss'
})
export class CatalogListsPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(CatalogStore);
  private readonly toast = inject(ToastService);

  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(16)] }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(128)]
    })
  });

  $tab = signal<string>(TAB_CATEGORIES);
  $dialogOpen = signal(false);
  $editingId = signal<number | undefined>(undefined);

  $isUnits = computed(() => this.$tab() === TAB_UNITS);
  $canManage = computed(() => this.permissions.has(PERMISSION.MasterDataManage));
  $isLoading = computed(() => this.store.categories.$isLoading() || this.store.units.$isLoading());
  $isSaving = computed(
    () => this.store.saveCategory.$isRunning() || this.store.saveUnit.$isRunning()
  );
  $tabs = computed<readonly TabItem[]>(() => [
    { id: TAB_CATEGORIES, label: this.i18n.t('catalog.categories') },
    { id: TAB_UNITS, label: this.i18n.t('catalog.units') }
  ]);
  $rows = computed<readonly ListRow[]>(() =>
    this.$isUnits()
      ? this.store.$units().map(unit => ({ id: unit.id, code: unit.code, name: unit.name }))
      : this.store
          .$categories()
          .map(category => ({ id: category.id, code: '', name: category.name }))
  );
  $columns = computed<readonly TableColumn<ListRow>[]>(() => {
    const columns: TableColumn<ListRow>[] = [];
    if (this.$isUnits()) {
      columns.push({
        key: 'code',
        type: 'code',
        header: this.i18n.t('catalog.code'),
        value: row => row.code,
        primary: true
      });
    }
    columns.push({
      key: 'name',
      header: this.i18n.t('catalog.name'),
      value: row => row.name,
      primary: !this.$isUnits()
    });
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
  $dialogHeading = computed(() => {
    const editing = this.$editingId() !== undefined;
    if (this.$isUnits()) {
      return this.i18n.t(editing ? 'catalog.editUnit' : 'catalog.newUnit');
    }
    return this.i18n.t(editing ? 'catalog.editCategory' : 'catalog.newCategory');
  });
  $serverErrors = computed(() => {
    const error = this.$isUnits()
      ? this.store.saveUnit.$operation().error
      : this.store.saveCategory.$operation().error;
    return error?.fieldErrors ? Object.values(error.fieldErrors).flat() : [];
  });

  private readonly showServerErrors = effect(() => {
    const error = this.$isUnits()
      ? this.store.saveUnit.$operation().error
      : this.store.saveCategory.$operation().error;
    if (error) {
      applyFieldErrors(this.form, error);
    }
  });

  ngOnInit(): void {
    this.store.ensureLoaded();
  }

  onTabChange(tab: string): void {
    this.$tab.set(tab);
  }

  onAdd(): void {
    this.openDialog(undefined, '', '');
  }

  onEdit(row: ListRow): void {
    this.openDialog(row.id, row.code, row.name);
  }

  onSave(): void {
    this.form.markAllAsTouched();
    if (
      this.form.invalid ||
      (this.$isUnits() && this.form.controls.code.value.trim().length === 0)
    ) {
      this.form.controls.code.markAsTouched();
      return;
    }
    const { code, name } = this.form.getRawValue();
    const id = this.$editingId();
    const save$ = this.$isUnits()
      ? this.store.saveUnit.run({ id, body: { code: code.trim(), name: name.trim() } })
      : this.store.saveCategory.run({ id, body: { name: name.trim() } });
    save$
      .pipe(
        tap(() => {
          this.$dialogOpen.set(false);
          this.toast.success(this.i18n.t('common.saved'));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private openDialog(id: number | undefined, code: string, name: string): void {
    this.store.saveCategory.reset();
    this.store.saveUnit.reset();
    this.form.reset({ code, name });
    this.$editingId.set(id);
    this.$dialogOpen.set(true);
  }
}
