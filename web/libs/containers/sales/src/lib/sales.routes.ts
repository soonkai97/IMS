import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { SalesOrderDetailPageComponent } from './sales-order-detail/sales-order-detail.component';
import { SalesOrderListPageComponent } from './sales-order-list/sales-order-list.component';

/**
 * Module sales: the module is asked first, so a module that is off is the not-found page, never the forbidden one; then the right
 * to read sales orders. The editor (PL3.9) and the delivery (PL3.10) add their routes under this parent.
 */
export const SALES_ORDER_ROUTES: Routes = [
  {
    path: '',
    canActivate: [requireModule(MODULE_CODE.Sales), requirePermission(PERMISSION.SoRead)],
    children: [
      { path: '', component: SalesOrderListPageComponent, title: 'so.title' },
      { path: ':id', component: SalesOrderDetailPageComponent, title: 'so.title' }
    ]
  }
];
