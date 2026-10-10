import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { SettingsPageComponent } from './settings/settings.component';
import { UsersPageComponent } from './users/users.component';

export const USER_ROUTES: Routes = [
  {
    path: '',
    component: UsersPageComponent,
    canActivate: [requirePermission(PERMISSION.UsersManage)]
  }
];

export const SETTINGS_ROUTES: Routes = [
  {
    path: '',
    component: SettingsPageComponent,
    canActivate: [requirePermission(PERMISSION.SettingsManage)]
  }
];
