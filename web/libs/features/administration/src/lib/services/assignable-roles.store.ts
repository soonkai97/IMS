import { Injectable, inject } from '@angular/core';
import { AssignableRoleDto, UsersClient } from '@ims/api';
import { QueryLoader } from '@ims/utils';

/**
 * Provided by the users page (not the root). The active roles the signed-in person may give to a user, filtered by the API with the
 * same rule as the user save; the page never decides which roles exist or which are allowed.
 */
@Injectable()
export class AssignableRolesStore {
  private readonly client = inject(UsersClient);

  readonly list = new QueryLoader<void, AssignableRoleDto[]>(() => this.client.assignableRoles());
}
