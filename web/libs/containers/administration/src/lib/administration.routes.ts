import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { PERMISSION } from '@ims/models';
import { RoleEditPageComponent } from './roles/role-edit.component';
import { RolesPageComponent } from './roles/roles.component';
import { SettingsPageComponent } from './settings/settings.component';
import { UsersPageComponent } from './users/users.component';

export const USER_ROUTES: Routes = [
  {
    path: '',
    component: UsersPageComponent,
    title: 'users.title',
    canActivate: [requirePermission(PERMISSION.UsersManage)]
  }
];

/** /roles/**: the list and the editor read with roles.read; creating a role needs roles.manage. The editor asks before leaving unsaved edits. */
export const ROLE_ROUTES: Routes = [
  {
    path: '',
    component: RolesPageComponent,
    title: 'roles.title',
    canActivate: [requirePermission(PERMISSION.RolesRead)]
  },
  {
    path: 'new',
    component: RoleEditPageComponent,
    title: 'roleEdit.newTitle',
    canActivate: [requirePermission(PERMISSION.RolesManage)],
    canDeactivate: [unsavedChangesGuard]
  },
  {
    path: ':id',
    component: RoleEditPageComponent,
    title: 'roles.title',
    canActivate: [requirePermission(PERMISSION.RolesRead)],
    canDeactivate: [unsavedChangesGuard]
  }
];

export const SETTINGS_ROUTES: Routes = [
  {
    path: '',
    component: SettingsPageComponent,
    title: 'settings.title',
    canActivate: [requirePermission(PERMISSION.SettingsManage)]
  }
];
