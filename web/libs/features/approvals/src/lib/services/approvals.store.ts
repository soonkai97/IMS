import { Injectable, computed, inject } from '@angular/core';
import { forkJoin, map, of, switchMap } from 'rxjs';
import { DocumentDto, DocumentStatus, DocumentsClient } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { DOCUMENT_ACTION, toDocumentActions } from '@ims/models';
import { QueryLoader } from '@ims/utils';

const INBOX_PAGE_SIZE = 50;

/**
 * Approvals inbox. The list endpoint has no "needs approval" flag, so the Submitted documents are opened one by one
 * (their `requiresApproval` and `allowedActions` decide which inbox tab they belong to). Oldest first.
 */
@Injectable({ providedIn: 'root' })
export class ApprovalsStore {
  private readonly client = inject(DocumentsClient);
  private readonly permissions = inject(PermissionService);

  readonly submitted = new QueryLoader<void, DocumentDto[]>(() =>
    this.client
      .list(
        undefined,
        DocumentStatus.Submitted,
        undefined,
        undefined,
        undefined,
        undefined,
        1,
        INBOX_PAGE_SIZE
      )
      .pipe(
        switchMap(page =>
          page.items.length === 0
            ? of([])
            : forkJoin(page.items.map(item => this.client.get(item.id)))
        ),
        map(documents => documents.filter(document => document.requiresApproval)),
        map(documents =>
          [...documents].sort((a, b) => (a.submittedAt ?? '').localeCompare(b.submittedAt ?? ''))
        )
      )
  );

  /** Documents the current user can approve or reject right now. */
  readonly $waitingForMe = computed(() =>
    (this.submitted.$data() ?? []).filter(document =>
      toDocumentActions(document.allowedActions).has(DOCUMENT_ACTION.Approve)
    )
  );
  /** Documents the current user submitted and that still wait for someone else. */
  readonly $submittedByMe = computed(() => {
    const username = this.permissions.$username();
    return (this.submitted.$data() ?? []).filter(document => document.submittedBy === username);
  });
  readonly $waitingCount = computed(() => this.$waitingForMe().length);

  refresh(): void {
    this.submitted.load();
  }
}
