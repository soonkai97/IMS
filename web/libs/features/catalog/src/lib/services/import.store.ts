import { Injectable, computed, inject, signal } from '@angular/core';
import { tap } from 'rxjs';
import { FileResponse, ImportClient, ImportResult } from '@ims/api';
import { ApiCommand, saveFile } from '@ims/utils';
import { IMPORT_KIND, ImportKind } from '../constants/import.const';

export interface ImportArgs {
  readonly kind: ImportKind;
  readonly file: File;
  /** false = validate only (preview); true = write to the database. */
  readonly commit: boolean;
}

/** Excel import with a preview step: nothing is saved until the preview has no error rows (D6). */
@Injectable({ providedIn: 'root' })
export class ImportStore {
  private readonly client = inject(ImportClient);

  /** Latest result: the preview while reviewing, then the committed outcome. */
  readonly $result = signal<ImportResult | undefined>(undefined);

  readonly run = new ApiCommand<ImportArgs, ImportResult>(args => {
    const file = { data: args.file, fileName: args.file.name };
    const call$ =
      args.kind === IMPORT_KIND.Products
        ? this.client.products(args.commit, file)
        : this.client.openingStock(args.commit, file);
    return call$.pipe(tap(result => this.$result.set(result)));
  });

  readonly downloadTemplate = new ApiCommand<ImportKind, FileResponse>(kind =>
    this.client.template(kind).pipe(tap(file => saveFile(file, `${kind}-template.xlsx`)))
  );

  readonly $errorRows = computed(() => this.$result()?.rows.filter(row => !row.ok) ?? []);
  readonly $hasErrors = computed(() => (this.$result()?.invalid ?? 0) > 0);

  reset(): void {
    this.$result.set(undefined);
    this.run.reset();
  }
}
