import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { AuditLogPageComponent } from './audit-log/audit-log.component';

export const AUDIT_ROUTES: Routes = [
  {
    path: '',
    component: AuditLogPageComponent,
    title: 'audit.title',
    canActivate: [requirePermission(PERMISSION.AuditRead)]
  }
];
