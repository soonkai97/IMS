import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { RolePageComponent } from './roles/role-page.component';
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
    path: ':id',
    component: RolePageComponent,
    title: 'roles.title',
    canActivate: [requirePermission(PERMISSION.RolesRead)]
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
