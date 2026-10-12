import { Injectable, computed, inject } from '@angular/core';
import { map } from 'rxjs';
import { DocumentListItem, DocumentStatus, DocumentsClient } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { SystemSettingsStore } from '@ims/core/settings';
import { DocumentsStore } from '@ims/features/documents';
import { MAX_PAGE_SIZE } from '@ims/models';
import { QueryLoader } from '@ims/utils';

/**
 * Approvals inbox: ONE list call (`status=Submitted&requiresApproval=true`), no per-document reads. The list items
 * carry the flags and the submitter, so the tabs are built from them; "waiting for me" is the server's `canAct`
 * (the same rule as the menu badge), never a role check in the browser. The number for badges and Home comes from
 * `GET /api/documents/counts`, which is what the server says I can act on. Oldest first, because the list endpoint
 * sorts newest first.
 */
@Injectable({ providedIn: 'root' })
export class ApprovalsStore {
  private readonly client = inject(DocumentsClient);
  private readonly documents = inject(DocumentsStore);
  private readonly permissions = inject(PermissionService);
  private readonly settings = inject(SystemSettingsStore);

  readonly submitted = new QueryLoader<void, DocumentListItem[]>(() =>
    this.client
      .list(
        undefined,
        DocumentStatus.Submitted,
        undefined,
        undefined,
        undefined,
        undefined,
        1,
        MAX_PAGE_SIZE,
        undefined,
        false,
        undefined,
        true
      )
      .pipe(map(page => [...page.items].sort(byOldestSubmission)))
  );

  /** Documents the current user can approve or reject right now. */
  readonly $waitingForMe = computed(() =>
    (this.submitted.$data() ?? []).filter(document => document.canAct)
  );
  /** Documents the current user submitted and that still wait for someone else. */
  readonly $submittedByMe = computed(() => {
    const userId = this.permissions.$userId();
    return (this.submitted.$data() ?? []).filter(document => document.submittedById === userId);
  });
  readonly $waitingCount = computed(() => this.documents.counts.$data()?.approvalsWaiting ?? 0);

  /** Reloads the inbox and the numbers. */
  refresh(): void {
    this.settings.ensureLoaded();
    this.submitted.load();
    this.documents.counts.load();
  }
}

function byOldestSubmission(a: DocumentListItem, b: DocumentListItem): number {
  return timeOf(a.submittedAt) - timeOf(b.submittedAt);
}

function timeOf(timestamp: string | undefined): number {
  return timestamp ? Date.parse(timestamp) : 0;
}
