import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { ReportsPageComponent } from './reports/reports.component';

export const REPORT_ROUTES: Routes = [
  {
    path: '',
    component: ReportsPageComponent,
    canActivate: [requirePermission(PERMISSION.ReportsRead)]
  }
];
