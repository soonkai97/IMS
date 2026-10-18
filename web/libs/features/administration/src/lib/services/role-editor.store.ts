import { Injectable, inject } from '@angular/core';
import { RoleDto, RolesClient } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { PublicId } from '@ims/models';
import { QueryLoader } from '@ims/utils';
import { RolesStore } from './roles.store';

/**
 * The role page's calls. Provided by the page (not the root) together with `RolesStore`, so what one signed-in person read is
 * gone with the page.
 */
@Injectable()
export class RoleEditorStore {
  private readonly client = inject(RolesClient);
  private readonly shared = inject(RolesStore);

  /** The permission catalog: the sections, the sentences and what each permission needs. */
  readonly catalog = this.shared.catalog;
  /** The screen shows a 404 itself (the role is gone, or was never there), so no toast. */
  readonly detail = new QueryLoader<PublicId, RoleDto>(id => this.client.get(id, handledInline()));
}
