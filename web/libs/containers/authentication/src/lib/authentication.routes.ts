import { Routes } from '@angular/router';
import { guestGuard } from '@ims/core/identity';
import { ChangePasswordPageComponent } from './change-password/change-password.component';
import { LoginPageComponent } from './login/login.component';

export const AUTHENTICATION_ROUTES: Routes = [
  { path: '', component: LoginPageComponent, title: 'login.title', canActivate: [guestGuard] }
];

/** The signed-in user's own account screens, inside the shell (so only a signed-in user reaches them). */
export const ACCOUNT_ROUTES: Routes = [
  { path: 'password', component: ChangePasswordPageComponent, title: 'account.password.title' }
];
