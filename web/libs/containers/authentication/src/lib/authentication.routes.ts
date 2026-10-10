import { Routes } from '@angular/router';
import { guestGuard } from '@ims/core/identity';
import { LoginPageComponent } from './login/login.component';

export const AUTHENTICATION_ROUTES: Routes = [
  { path: '', component: LoginPageComponent, canActivate: [guestGuard] }
];
