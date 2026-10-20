import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { AccountingExportStore } from '@ims/features/reports';
import { WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiDateFieldComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiSelectComponent
} from '@ims/ui';
import { controlSignal, focusFirstInvalid, isDateRangeInvalid } from '@ims/utils';

/**
 * Reports > Accounting export (module accounting, permission `accounting.export`): the posted lines of a period as one .xlsx in a fixed
 * layout, at cost. The period is required; the warehouse is optional and lists only the warehouses the person may read. The server
 * names the file and refuses, in words, a period with more rows than one file holds (never a short file), and the screen shows every
 * failure of the call itself in one alert, never as a toast.
 */
@Component({
  selector: 'app-accounting-export-page',
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiDateFieldComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiSelectComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './accounting-export.component.html',
  styleUrl: './accounting-export.component.scss'
})
export class AccountingExportPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly permissions = inject(PermissionService);
  private readonly store = inject(AccountingExportStore);
  private readonly warehouses = inject(WarehousesStore);

  readonly form = new FormGroup({
    from: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    to: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    warehouseId: new FormControl<PublicId | null>(null)
  });

  private readonly $from = controlSignal(this.form.controls.from);
  private readonly $to = controlSignal(this.form.controls.to);

  $busy = this.store.download.$isRunning;
  $lastFileName = this.store.$lastFileName;
  $rangeInvalid = computed(() => isDateRangeInvalid(this.$from(), this.$to()));
  $toError = computed(() => (this.$rangeInvalid() ? this.i18n.t('filter.rangeInvalid') : null));
  /** The sentence of each reason the last download failed (the row cap, a warehouse that is not there, no permission, no network). */
  $problems = computed<readonly string[]>(() => {
    const error = this.store.download.$operation().error;
    if (!error) {
      return [];
    }
    const fields = Object.values(error.fieldErrors).flat();
    return fields.length > 0 ? fields : [error.message ?? error.detail];
  });
  $canExport = computed(() => this.permissions.has(PERMISSION.AccountingExport));
  $warehouseOptions = computed<readonly SelectOption[]>(() =>
    this.warehouses
      .$activeWarehouses()
      .map(warehouse => ({ value: warehouse.id, label: warehouse.name }))
  );

  ngOnInit(): void {
    this.warehouses.ensureLoaded();
    // The last answer (a failure, or "Downloaded") belongs to the last request, not to the next period.
    this.store.download.reset();
    this.store.$lastFileName.set(undefined);
  }

  onDownload(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.$rangeInvalid() || !this.$canExport()) {
      // The focus goes to the first field to fix, not to stay on Download (WCAG 3.3.1).
      afterNextRender(() => focusFirstInvalid(this.host.nativeElement), {
        injector: this.injector
      });
      return;
    }
    const { from, to, warehouseId } = this.form.getRawValue();
    this.store.$lastFileName.set(undefined);
    this.store.download
      .run({ from, to, warehouseId: warehouseId ?? undefined })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }
}
