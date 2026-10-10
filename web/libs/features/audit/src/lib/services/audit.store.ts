import { Injectable, inject } from '@angular/core';
import { AuditClient, PagedResultOfAuditLogDto } from '@ims/api';
import { PagedQuery } from '@ims/models';
import { QueryLoader } from '@ims/utils';

export interface AuditQuery extends PagedQuery {
  readonly entityType?: string;
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
      query.entityType,
      query.actor,
      query.from,
      query.to,
      query.page,
      query.pageSize
    )
  );
}
