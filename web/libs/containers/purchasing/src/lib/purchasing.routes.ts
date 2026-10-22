import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail/purchase-order-detail.component';
import { PurchaseOrderEditorPageComponent } from './purchase-order-editor/purchase-order-editor.component';
import { PurchaseOrderPrintPageComponent } from './purchase-order-print/purchase-order-print.component';
import { PurchaseOrderListPageComponent } from './purchase-order-list/purchase-order-list.component';
import { ReorderPageComponent } from './reorder/reorder.component';

/** `requirePermission` asks for ANY of its permissions, so the two rights are two guards. */
const EDITOR_GUARDS = [
  requirePermission(PERMISSION.PoManage),
  requirePermission(PERMISSION.CostView)
];

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
      // Reading what to order needs po.read only (the parent); creating from it needs the editor's two rights, checked on the page.
      { path: 'reorder', component: ReorderPageComponent, title: 'reorder.title' },
      // The editor is for a holder of po.manage AND cost.view (an order carries prices: without the right the API refuses the save); `new` comes before `:id`, which would take it for an order.
      {
        path: 'new',
        component: PurchaseOrderEditorPageComponent,
        title: 'po.new',
        canActivate: EDITOR_GUARDS,
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: ':id/edit',
        component: PurchaseOrderEditorPageComponent,
        title: 'po.title',
        canActivate: EDITOR_GUARDS,
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: ':id/print',
        component: PurchaseOrderPrintPageComponent,
        title: 'po.print',
        canActivate: [requirePermission(PERMISSION.CostView)]
      },
      { path: ':id', component: PurchaseOrderDetailPageComponent, title: 'po.title' }
    ]
  }
];
