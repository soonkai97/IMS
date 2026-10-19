import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  untracked,
  inject,
  input
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { ProductDto, ProductSaveRequest } from '@ims/api';
import { TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { HasUnsavedChanges } from '@ims/core/navigation';
import { ToastService } from '@ims/core/notification';
import { I18nService } from '@ims/core/i18n';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import { NO_PUBLIC_ID, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiCheckboxComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiSelectComponent,
  UiSkeletonComponent
} from '@ims/ui';
import { applyFieldErrors, normalizePublicId } from '@ims/utils';

type ConversionGroup = FormGroup<{
  fromUomId: FormControl<PublicId | null>;
  factor: FormControl<number | null>;
}>;

const MAX_SAFETY_STOCK = 1_000_000_000;

@Component({
  selector: 'app-product-edit-page',
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiCheckboxComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiSelectComponent,
    UiSkeletonComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-edit.component.html',
  styleUrl: './product-edit.component.scss'
})
export class ProductEditPageComponent implements OnInit, HasUnsavedChanges {
  private readonly catalog = inject(CatalogStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly store = inject(ProductsStore);
  private readonly toast = inject(ToastService);

  /** Route parameter; absent on /products/new. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly newBarcode = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    sku: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(64)]
    }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)]
    }),
    categoryId: new FormControl<PublicId | null>(null),
    baseUomId: new FormControl<PublicId | null>(null, [Validators.required]),
    safetyStock: new FormControl<number | null>(0, [
      Validators.required,
      Validators.min(0),
      Validators.max(MAX_SAFETY_STOCK)
    ]),
    isActive: new FormControl(true, { nonNullable: true }),
    barcodes: new FormControl<readonly string[]>([], { nonNullable: true }),
    conversions: new FormArray<ConversionGroup>([])
  });

  /** The id in the URL as the API writes it; undefined on `/products/new` and for anything that is not a GUID. */
  $productId = computed(() => normalizePublicId(this.$id()));
  $isNew = computed(() => this.$id() === undefined);
  /** An old bookmark (`/products/123`): no product, no request, and nothing to save. */
  $notFound = computed(() => !this.$isNew() && this.$productId() === undefined);
  $canManage = computed(() => this.permissions.has(PERMISSION.MasterDataManage));
  $isLoading = this.store.detail.$isLoading;
  $isSaving = this.store.save.$isRunning;
  $unitOptions = computed<readonly SelectOption[]>(() =>
    this.catalog.$units().map(unit => ({ value: unit.id, label: `${unit.code} - ${unit.name}` }))
  );
  $categoryOptions = computed<readonly SelectOption[]>(() =>
    this.catalog.$categories().map(category => ({ value: category.id, label: category.name }))
  );
  $heading = computed(() => {
    if (this.$isNew()) {
      return this.i18n.t('product.new');
    }
    return this.$notFound()
      ? this.i18n.t('notFound.title')
      : (this.store.detail.$data()?.sku ?? '');
  });

  /** Messages of a failed save that belong to no field. */
  $generalErrors = computed(() => {
    const error = this.store.save.$operation().error;
    if (!error) {
      return [];
    }
    return Object.entries(error.fieldErrors)
      .filter(([field]) => !this.form.get(field))
      .flatMap(([, messages]) => messages);
  });

  /**
   * Opens the product the URL names, and again when the same page is reused for another one (browser Back/Forward
   * between two products): reading the id once left the form on the previous product (EA-20).
   */
  private readonly openProduct = effect(() => {
    const id = this.$productId();
    untracked(() => {
      if (id === undefined) {
        this.store.detail.reset();
        return;
      }
      this.store.detail.load(id);
    });
  });

  private readonly populateForm = effect(() => {
    const product = this.store.detail.$data();
    if (product && product.id === this.$productId()) {
      this.patch(product);
    }
  });

  private readonly showServerErrors = effect(() => {
    const error = this.store.save.$operation().error;
    if (error) {
      applyFieldErrors(this.form, error);
    }
  });

  ngOnInit(): void {
    this.catalog.ensureLoaded();
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  onAddBarcode(): void {
    const code = this.newBarcode.value.trim();
    const current = this.form.controls.barcodes.value;
    if (code.length === 0 || current.includes(code)) {
      this.newBarcode.reset('');
      return;
    }
    this.form.controls.barcodes.setValue([...current, code]);
    this.form.controls.barcodes.markAsDirty();
    this.newBarcode.reset('');
  }

  onRemoveBarcode(code: string): void {
    this.form.controls.barcodes.setValue(
      this.form.controls.barcodes.value.filter(item => item !== code)
    );
    this.form.controls.barcodes.markAsDirty();
  }

  onAddConversion(): void {
    this.form.controls.conversions.push(this.createConversion(null, null));
    this.form.controls.conversions.markAsDirty();
  }

  onRemoveConversion(index: number): void {
    this.form.controls.conversions.removeAt(index);
    this.form.controls.conversions.markAsDirty();
  }

  onSave(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || !this.$canManage() || this.$notFound()) {
      return;
    }
    this.store.save
      .run({ id: this.$productId(), body: this.toRequest() })
      .pipe(
        tap(product => {
          this.form.markAsPristine();
          this.toast.success(this.i18n.t('product.saved', { sku: product.sku }));
          if (this.$isNew()) {
            void this.router.navigate(['/products', product.id], {
              replaceUrl: true,
              scroll: 'manual'
            });
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private patch(product: ProductDto): void {
    this.form.patchValue({
      sku: product.sku,
      name: product.name,
      categoryId: product.categoryId ?? null,
      baseUomId: product.baseUomId,
      safetyStock: product.safetyStock,
      isActive: product.isActive,
      barcodes: product.barcodes
    });
    this.form.controls.conversions.clear();
    for (const conversion of product.conversions) {
      this.form.controls.conversions.push(
        this.createConversion(conversion.fromUomId, conversion.factor)
      );
    }
    this.form.markAsPristine();
    if (!this.$canManage()) {
      this.form.disable();
    }
  }

  private createConversion(fromUomId: PublicId | null, factor: number | null): ConversionGroup {
    return new FormGroup({
      fromUomId: new FormControl<PublicId | null>(fromUomId, [Validators.required]),
      factor: new FormControl<number | null>(factor, [Validators.required, Validators.min(0.0001)])
    });
  }

  private toRequest(): ProductSaveRequest {
    const value = this.form.getRawValue();
    const baseUomId = value.baseUomId ?? NO_PUBLIC_ID;
    return {
      sku: value.sku.trim(),
      name: value.name.trim(),
      categoryId: value.categoryId ?? undefined,
      baseUomId,
      safetyStock: value.safetyStock ?? 0,
      isActive: value.isActive,
      barcodes: [...value.barcodes],
      conversions: value.conversions.map(conversion => ({
        fromUomId: conversion.fromUomId ?? NO_PUBLIC_ID,
        toUomId: baseUomId,
        factor: conversion.factor ?? 0
      }))
    };
  }
}
