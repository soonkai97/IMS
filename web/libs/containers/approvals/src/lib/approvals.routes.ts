import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { ApprovalsInboxPageComponent } from './inbox/inbox.component';

export const APPROVAL_ROUTES: Routes = [
  {
    path: '',
    component: ApprovalsInboxPageComponent,
    canActivate: [requirePermission(PERMISSION.DocumentApprove)]
  }
];
