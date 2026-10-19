import { Injectable, inject } from '@angular/core';
import { AssignableRoleDto, UsersClient } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { QueryLoader } from '@ims/utils';

/**
 * Provided by the users page (not the root). The active roles the signed-in person may give to a user, filtered by the API with the
 * same rule as the user save; the page never decides which roles exist or which are allowed. A failed load is shown by the page's
 * own banner with Retry, so the global toast is switched off for it (one announcement).
 */
@Injectable()
export class AssignableRolesStore {
  private readonly client = inject(UsersClient);

  readonly list = new QueryLoader<void, AssignableRoleDto[]>((_, context) =>
    this.client.assignableRoles(context)
  );

  load(): void {
    this.list.load(undefined, handledInline());
  }
}
