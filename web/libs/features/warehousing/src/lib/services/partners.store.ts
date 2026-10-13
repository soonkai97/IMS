import { Injectable, computed, inject } from '@angular/core';
import { tap } from 'rxjs';
import {
  PagedResultOfPartnerDto,
  PartnerDto,
  PartnerSaveRequest,
  PartnerType,
  PartnersClient
} from '@ims/api';
import { handledInline } from '@ims/core/http';
import { HTTP_STATUS, MAX_PAGE_SIZE, PagedQuery, PublicId } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface PartnerQuery extends PagedQuery {
  readonly type?: PartnerType;
  readonly includeInactive: boolean;
  readonly search?: string;
}

export interface SavePartnerArgs {
  readonly id?: PublicId;
  readonly body: PartnerSaveRequest;
}

/** The partner dialog shows "name already used" (409) at the Name field, so that answer raises no toast. */
const NAME_TAKEN_SHOWN = handledInline(HTTP_STATUS.Conflict);

@Injectable({ providedIn: 'root' })
export class PartnersStore {
  private readonly client = inject(PartnersClient);

  readonly list = new QueryLoader<PartnerQuery, PagedResultOfPartnerDto>(query =>
    this.client.list(query.type, query.includeInactive, query.search, query.page, query.pageSize)
  );
  readonly save = new ApiCommand<SavePartnerArgs, PartnerDto>(args =>
    (args.id === undefined
      ? this.client.create(args.body, NAME_TAKEN_SHOWN)
      : this.client.update(args.id, args.body, NAME_TAKEN_SHOWN)
    ).pipe(tap(() => this.list.reload()))
  );

  readonly $partners = computed(() => this.list.$data()?.items ?? []);

  /** Loads the active partners of one type for a document form (suppliers for receipts, customers for issues). */
  loadActive(type: PartnerType): void {
    this.list.load({ type, includeInactive: false, page: 1, pageSize: MAX_PAGE_SIZE });
  }
}
