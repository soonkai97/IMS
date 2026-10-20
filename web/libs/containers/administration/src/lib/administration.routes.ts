import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { PERMISSION } from '@ims/models';
import { RolePageComponent } from './roles/role-page.component';
import { ModulesPageComponent } from './modules/modules.component';
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

export const ROLE_ROUTES: Routes = [
  {
    path: '',
    component: RolesPageComponent,
    title: 'roles.title',
    canActivate: [requirePermission(PERMISSION.RolesRead)]
  },
  {
    path: 'new',
    component: RolePageComponent,
    title: 'rolePage.newTitle',
    canActivate: [requirePermission(PERMISSION.RolesManage)],
    canDeactivate: [unsavedChangesGuard]
  },
  {
    path: ':id',
    component: RolePageComponent,
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
  },
  {
    path: 'modules',
    component: ModulesPageComponent,
    title: 'modules.title',
    canActivate: [requirePermission(PERMISSION.ModulesManage)]
  }
];
