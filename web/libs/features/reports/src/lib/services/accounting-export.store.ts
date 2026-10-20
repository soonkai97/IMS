import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { AccountingClient, FileResponse } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { PublicId } from '@ims/models';
import { ApiCommand, saveFile } from '@ims/utils';

export interface AccountingExportQuery {
  /** First day of the period, `yyyy-MM-dd`. */
  readonly from: string;
  /** Last day of the period, `yyyy-MM-dd`. */
  readonly to: string;
  readonly warehouseId?: PublicId;
}

const FALLBACK_NAME = 'ims-accounting.xlsx';

/**
 * The accounting file of a period (module accounting, permission `accounting.export`). The server names the file
 * (Content-Disposition) and refuses, in words, a period that has more rows than one file holds, so the screen shows every failure
 * of the call itself (`handledInline`): no toast. Exclusive: a double click sends one request.
 */
@Injectable({ providedIn: 'root' })
export class AccountingExportStore {
  private readonly client = inject(AccountingClient);

  /** The name of the last file that was handed to the browser, for the confirmation line. */
  readonly $lastFileName = signal<string | undefined>(undefined);

  readonly download = new ApiCommand<AccountingExportQuery, FileResponse>(
    query =>
      this.save(this.client.export(query.from, query.to, query.warehouseId, handledInline())),
    true
  );

  private save(source$: Observable<FileResponse>): Observable<FileResponse> {
    return source$.pipe(
      tap(file => {
        saveFile(file, FALLBACK_NAME);
        this.$lastFileName.set(file.fileName ?? FALLBACK_NAME);
      })
    );
  }
}
