import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail/purchase-order-detail.component';
import { PurchaseOrderListPageComponent } from './purchase-order-list/purchase-order-list.component';

/**
 * Module purchasing: the module is asked first, so a module that is off is the not-found page, never the forbidden one; then the
 * right to read purchase orders. Both pages sit under one guarded parent.
 */
export const PURCHASE_ORDER_ROUTES: Routes = [
  {
    path: '',
    canActivate: [requireModule(MODULE_CODE.Purchasing), requirePermission(PERMISSION.PoRead)],
    children: [
      { path: '', component: PurchaseOrderListPageComponent, title: 'po.title' },
      { path: ':id', component: PurchaseOrderDetailPageComponent, title: 'po.title' }
    ]
  }
];
