import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { AccountingExportPageComponent } from './accounting-export/accounting-export.component';
import { ReportsPageComponent } from './reports/reports.component';

export const REPORT_ROUTES: Routes = [
  {
    path: '',
    component: ReportsPageComponent,
    title: 'reports.title',
    canActivate: [requirePermission(PERMISSION.ReportsRead)]
  },
  {
    // Module accounting: the module is asked first, so a module that is off is the not-found page, never the forbidden one.
    path: 'accounting',
    component: AccountingExportPageComponent,
    title: 'accounting.title',
    canActivate: [
      requireModule(MODULE_CODE.Accounting),
      requirePermission(PERMISSION.AccountingExport)
    ]
  }
];
