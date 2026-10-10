import { Injectable, inject } from '@angular/core';
import { AuditClient, AuditFilterOptions, PagedResultOfAuditLogDto } from '@ims/api';
import { PagedQuery } from '@ims/models';
import { QueryLoader } from '@ims/utils';

export interface AuditQuery extends PagedQuery {
  /** One box: an entity name fragment ("doc") or an id prefix (a document number) (PS-06). */
  readonly search?: string;
  /** Starts with: "document.", "login", "document.posted". */
  readonly action?: string;
  readonly actor?: string;
  readonly from?: string;
  readonly to?: string;
  readonly page: number;
  readonly pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class AuditStore {
  private readonly client = inject(AuditClient);

  readonly list = new QueryLoader<AuditQuery, PagedResultOfAuditLogDto>(query =>
    this.client.list(
      undefined,
      query.actor,
      query.from,
      query.to,
      query.page,
      query.pageSize,
      undefined,
      undefined,
      undefined,
      query.action,
      query.search
    )
  );
  /** The entity and action names an audit row can carry, for the filter dropdown. */
  readonly filters = new QueryLoader<void, AuditFilterOptions>(() => this.client.filters());
}
