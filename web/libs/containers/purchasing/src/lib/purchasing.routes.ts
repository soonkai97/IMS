import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail/purchase-order-detail.component';
import { PurchaseOrderEditorPageComponent } from './purchase-order-editor/purchase-order-editor.component';
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
      // The editor is for a holder of po.manage; `new` comes before `:id`, which would take it for an order.
      {
        path: 'new',
        component: PurchaseOrderEditorPageComponent,
        title: 'po.new',
        canActivate: [requirePermission(PERMISSION.PoManage)],
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: ':id/edit',
        component: PurchaseOrderEditorPageComponent,
        title: 'po.title',
        canActivate: [requirePermission(PERMISSION.PoManage)],
        canDeactivate: [unsavedChangesGuard]
      },
      { path: ':id', component: PurchaseOrderDetailPageComponent, title: 'po.title' }
    ]
  }
];
