import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import {
  FORBIDDEN_ROUTE,
  HOME_ROUTE,
  LOGIN_ROUTE,
  RETURN_URL_PARAM
} from '../constants/identity.const';
import { AuthService } from '../services/auth.service';
import { PermissionService } from '../services/permission.service';

/** Signed-in users only; everyone else goes to the login page and comes back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.$isAuthenticated()) {
    return true;
  }
  return router.createUrlTree([LOGIN_ROUTE], { queryParams: { [RETURN_URL_PARAM]: state.url } });
};

/** The login page for signed-out users only. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.$isAuthenticated() ? router.createUrlTree([HOME_ROUTE]) : true;
};

/** Route needs at least one of the permissions; otherwise the 403 page. */
export function requirePermission(...permissions: string[]): CanActivateFn {
  return () => {
    const permissionService = inject(PermissionService);
    const router = inject(Router);
    return permissionService.hasAny(permissions) ? true : router.createUrlTree([FORBIDDEN_ROUTE]);
  };
}
