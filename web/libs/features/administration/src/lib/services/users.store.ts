import { Injectable, inject } from '@angular/core';
import { tap } from 'rxjs';
import { PagedResultOfUserDto, UserDto, UserSaveRequest, UsersClient } from '@ims/api';
import { PagedQuery } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface UserQuery extends PagedQuery {
  readonly search?: string;
  readonly role?: string;
  readonly active?: boolean;
}

export interface SaveUserArgs {
  readonly id?: number;
  readonly body: UserSaveRequest;
}

@Injectable({ providedIn: 'root' })
export class UsersStore {
  private readonly client = inject(UsersClient);

  readonly list = new QueryLoader<UserQuery, PagedResultOfUserDto>(query =>
    this.client.list(query.search, query.role, query.active, query.page, query.pageSize)
  );
  readonly save = new ApiCommand<SaveUserArgs, UserDto>(args =>
    (args.id === undefined
      ? this.client.create(args.body)
      : this.client.update(args.id, args.body)
    ).pipe(tap(() => this.list.reload()))
  );
}
