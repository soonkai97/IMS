import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { DocumentDto, DocumentsClient, ReturnsClient } from '@ims/api';
import { HANDLED_INLINE } from '@ims/core/http';
import { HTTP_STATUS, PublicId } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

/**
 * Returns (module returns, permission returns.create). `start` makes the Draft return of a Posted document; `original` reads the
 * document a return belongs to, for the figures of its lines (returned, returnable). Both keep nothing for anyone else: the page
 * resets them whenever it opens.
 */
@Injectable({ providedIn: 'root' })
export class ReturnsStore {
  private readonly returns = inject(ReturnsClient);
  private readonly documents = inject(DocumentsClient);

  /** The page loads it with `handledInline()`; the context goes to the call. */
  readonly original = new QueryLoader<PublicId, DocumentDto>((id, context) =>
    this.documents.get(id, context)
  );

  /** A refusal (409: nothing left to return, the original is no longer posted, the module is off; 403) is the page's alert. */
  readonly start: ApiCommand<PublicId, DocumentDto> = new ApiCommand(
    originalDocumentId =>
      this.returns.create(
        { originalDocumentId },
        new HttpContext().set(HANDLED_INLINE, [HTTP_STATUS.Forbidden, HTTP_STATUS.Conflict])
      ),
    true
  );

  resetAll(): void {
    this.original.reset();
    this.start.reset();
  }
}
