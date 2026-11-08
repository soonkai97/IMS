import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { SalesOrderDetailPageComponent } from './sales-order-detail/sales-order-detail.component';
import { SalesOrderEditorPageComponent } from './sales-order-editor/sales-order-editor.component';
import { SalesOrderPrintPageComponent } from './sales-order-print/sales-order-print.component';
import { SalesOrderListPageComponent } from './sales-order-list/sales-order-list.component';

/**
 * Module sales: the module is asked first, so a module that is off is the not-found page, never the forbidden one; then the right
 * to read sales orders. The editor and the customer copy (selling prices) are for a holder of so.manage; the delivery (PL3.10)
 * adds its route under this parent.
 */
export const SALES_ORDER_ROUTES: Routes = [
  {
    path: '',
    canActivate: [requireModule(MODULE_CODE.Sales), requirePermission(PERMISSION.SoRead)],
    children: [
      { path: '', component: SalesOrderListPageComponent, title: 'so.title' },
      // `new` comes before `:id`, which would take it for an order.
      {
        path: 'new',
        component: SalesOrderEditorPageComponent,
        title: 'sales.new',
        canActivate: [requirePermission(PERMISSION.SoManage)],
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: ':id/edit',
        component: SalesOrderEditorPageComponent,
        title: 'so.title',
        canActivate: [requirePermission(PERMISSION.SoManage)],
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: ':id/print',
        component: SalesOrderPrintPageComponent,
        title: 'sales.print',
        canActivate: [requirePermission(PERMISSION.SoManage)]
      },
      { path: ':id', component: SalesOrderDetailPageComponent, title: 'so.title' }
    ]
  }
];
