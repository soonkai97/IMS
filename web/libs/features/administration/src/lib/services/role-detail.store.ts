import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of } from 'rxjs';
import {
  RoleCreateRequest,
  RoleDto,
  RolePermissionsResult,
  RoleUpdateRequest,
  RolesClient
} from '@ims/api';
import { HANDLED_INLINE, handledInline, withIfMatch } from '@ims/core/http';
import { PublicId } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';
import { RolesStore } from './roles.store';

export interface SaveRoleDetailsArgs {
  readonly id: PublicId;
  /** The concurrency token the edit started from, sent as `If-Match`. */
  readonly token: string;
  readonly body: RoleUpdateRequest;
}

export interface SavePermissionsArgs {
  readonly id: PublicId;
  readonly token: string;
  readonly permissions: readonly string[];
}

/** The screen shows every failure of these calls in place, so none of them raises a toast as well. */
function inline(token?: string): HttpContext {
  return withIfMatch(token).set(HANDLED_INLINE, 'all');
}

/**
 * The role editor's calls. Provided by the editor page (not the root) together with `RolesStore`, so what one signed-in
 * person read is gone with the page.
 */
@Injectable()
export class RoleDetailStore {
  private readonly client = inject(RolesClient);
  private readonly shared = inject(RolesStore);

  /** The permission catalog and the active roles (the "copy from" choices). */
  readonly catalog = this.shared.catalog;
  readonly roleChoices = this.shared.list;

  readonly detail = new QueryLoader<PublicId, RoleDto>(id => this.client.get(id, handledInline()));
  readonly create = new ApiCommand<RoleCreateRequest, RoleDto>(
    body => this.client.create(body, inline()),
    true
  );
  readonly saveDetails = new ApiCommand<SaveRoleDetailsArgs, RoleDto>(
    args => this.client.update(args.id, args.body, inline(args.token)),
    true
  );
  readonly savePermissions = new ApiCommand<SavePermissionsArgs, RolePermissionsResult>(
    args =>
      this.client.replacePermissions(
        args.id,
        { permissions: [...args.permissions] },
        inline(args.token)
      ),
    true
  );
  readonly remove = new ApiCommand<PublicId, void>(id => this.client.delete(id, inline()), true);

  /**
   * Asked after a 409: the API gives no code for "stale" as against "locked" or "in use", but a second read tells: another
   * concurrency token than the one the edit started from means someone changed the role. A failed read says "not changed", so
   * the server's own sentence is shown.
   */
  changedSince(id: PublicId, token: string): Observable<boolean> {
    return this.client.get(id, inline()).pipe(
      map(role => role.concurrencyToken !== token),
      catchError(() => of(false))
    );
  }
}
