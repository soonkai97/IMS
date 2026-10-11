import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  computed,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { tap } from 'rxjs';
import { ImportRowResult, ImportRowStatus } from '@ims/api';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import {
  IMPORT_EXTENSION,
  IMPORT_KIND,
  ImportKind,
  ImportStore,
  MAX_IMPORT_BYTES
} from '@ims/features/catalog';
import { DEFAULT_PAGE_SIZE } from '@ims/models';
import {
  TabItem,
  TableColumn,
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiCellDirective,
  UiCheckboxComponent,
  UiEmptyStateComponent,
  UiFileInputComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiPagerComponent,
  UiStatusChipComponent,
  UiTableComponent,
  UiTabsComponent
} from '@ims/ui';
import { controlSignal, createPageState, saveCsv, toCsv } from '@ims/utils';

const ERROR_LIST_FILE = 'import-errors.csv';

/**
 * Excel import in four steps: template, upload, preview, confirm. The preview validates every row without saving;
 * the confirm button stays disabled until it has no error rows (all-or-nothing, D6).
 */
@Component({
  selector: 'app-product-import-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiCellDirective,
    UiCheckboxComponent,
    UiEmptyStateComponent,
    UiFileInputComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiPagerComponent,
    UiStatusChipComponent,
    UiTableComponent,
    UiTabsComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-import.component.html',
  styleUrl: './product-import.component.scss'
})
export class ProductImportPageComponent implements OnDestroy {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly store = inject(ImportStore);
  private readonly toast = inject(ToastService);

  readonly accept = IMPORT_EXTENSION;
  readonly rowStatus = ImportRowStatus;
  readonly pageSize = DEFAULT_PAGE_SIZE;

  $kind = signal<string>(IMPORT_KIND.Products);
  $file = signal<File | undefined>(undefined);
  $fileError = signal<string | undefined>(undefined);
  readonly errorsOnlyControl = new FormControl(true, { nonNullable: true });
  $errorsOnly = controlSignal(this.errorsOnlyControl);
  private readonly pageState = createPageState(computed(() => String(this.$errorsOnly())));
  $page = this.pageState.$page;

  $result = this.store.$result;
  $isRunning = this.store.run.$isRunning;
  $isDownloading = this.store.downloadTemplate.$isRunning;
  $hasErrors = this.store.$hasErrors;
  $isCommitted = computed(() => this.$result()?.committed === true);
  $isPreview = computed(() => this.$result() !== undefined && !this.$isCommitted());
  $canConfirm = computed(
    () => this.$isPreview() && !this.$hasErrors() && (this.$result()?.valid ?? 0) > 0
  );
  $isProducts = computed(() => this.$kind() === IMPORT_KIND.Products);

  $tabs = computed<readonly TabItem[]>(() => [
    { id: IMPORT_KIND.Products, label: this.i18n.t('import.tab.products') },
    { id: IMPORT_KIND.OpeningStock, label: this.i18n.t('import.tab.openingStock') }
  ]);
  $rows = computed<readonly ImportRowResult[]>(() => {
    const rows = this.$result()?.rows ?? [];
    return this.$errorsOnly() ? rows.filter(row => !row.ok) : rows;
  });
  $pageRows = computed(() => {
    const start = (this.$page() - 1) * DEFAULT_PAGE_SIZE;
    return this.$rows().slice(start, start + DEFAULT_PAGE_SIZE);
  });
  $columns = computed<readonly TableColumn<ImportRowResult>[]>(() => [
    {
      key: 'row',
      type: 'code',
      header: this.i18n.t('import.col.row'),
      value: row => String(row.row),
      primary: true
    },
    { key: 'key', header: this.i18n.t('import.col.key'), value: row => row.key },
    { key: 'status', type: 'status', header: this.i18n.t('import.col.status') },
    {
      key: 'problems',
      header: this.i18n.t('import.col.problems'),
      value: row => row.errors.join(' ')
    }
  ]);
  $createdSummary = computed(() => {
    const result = this.$result();
    return result
      ? this.i18n.t('import.done.summary', { created: result.created, updated: result.updated })
      : '';
  });

  ngOnDestroy(): void {
    this.store.reset();
  }

  onKindChange(kind: string): void {
    this.$kind.set(kind);
    this.onReset();
  }

  onDownloadTemplate(): void {
    this.store.downloadTemplate
      .run(this.kind())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  onFileSelected(file: File): void {
    this.$fileError.set(undefined);
    const isExcel = file.name.toLowerCase().endsWith(IMPORT_EXTENSION);
    if (!isExcel || file.size > MAX_IMPORT_BYTES) {
      this.$fileError.set(this.i18n.t('import.error.file'));
      return;
    }
    this.$file.set(file);
    this.errorsOnlyControl.setValue(true);
    this.run(file, false);
  }

  onConfirm(): void {
    const file = this.$file();
    if (file && this.$canConfirm()) {
      this.run(file, true);
    }
  }

  onReset(): void {
    this.store.reset();
    this.$file.set(undefined);
    this.$fileError.set(undefined);
  }

  onDownloadErrors(): void {
    const rows = this.store
      .$errorRows()
      .flatMap(row => row.errors.map(problem => [row.row, row.key, problem]));
    saveCsv(
      toCsv(
        [
          this.i18n.t('import.col.row'),
          this.i18n.t('import.col.key'),
          this.i18n.t('import.col.problems')
        ],
        rows
      ),
      ERROR_LIST_FILE
    );
  }

  onPageChange(page: number): void {
    this.pageState.set(page);
  }

  private kind(): ImportKind {
    return this.$kind() === IMPORT_KIND.OpeningStock
      ? IMPORT_KIND.OpeningStock
      : IMPORT_KIND.Products;
  }

  private run(file: File, commit: boolean): void {
    this.store.run
      .run({ kind: this.kind(), file, commit })
      .pipe(
        tap(result => {
          if (result.committed) {
            this.toast.success(this.i18n.t('import.done.toast'));
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }
}
