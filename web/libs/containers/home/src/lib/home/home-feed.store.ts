import { Injectable, inject } from '@angular/core';
import { DocumentStatus, DocumentsClient, PagedResultOfDocumentListItem } from '@ims/api';
import { QueryLoader } from '@ims/utils';

/** The newest drafts of the user: enough to find a returned one without paging (the count above it is the truth). */
const DRAFT_FEED_SIZE = 25;
const FIRST_PAGE = 1;

/**
 * What Home lists besides numbers: the user's own drafts, receipts and issues together in ONE call. The API has no
 * "returned" filter, so Home keeps the drafts with `isReturned` (docs/design/lean-scope.md: B1 waits).
 */
@Injectable({ providedIn: 'root' })
export class HomeFeedStore {
  private readonly client = inject(DocumentsClient);

  readonly drafts = new QueryLoader<void, PagedResultOfDocumentListItem>((_, context) =>
    this.client.list(
      undefined,
      DocumentStatus.Draft,
      undefined,
      undefined,
      undefined,
      undefined,
      FIRST_PAGE,
      DRAFT_FEED_SIZE,
      undefined,
      undefined,
      true,
      undefined,
      context
    )
  );
}
