import { Routes } from '@angular/router';
import { requireModule, requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { PriceListEditorPageComponent } from './price-list-editor/price-list-editor.component';
import { PriceListListPageComponent } from './price-list-list/price-list-list.component';

/**
 * Price lists (module sales): the module is asked first, so a module that is off is the not-found page, never the forbidden one;
 * then pricelist.manage, which every price-list endpoint needs (a price is shown to no one else). `new` comes before `:id`.
 */
export const PRICE_LIST_ROUTES: Routes = [
  {
    path: '',
    canActivate: [requireModule(MODULE_CODE.Sales), requirePermission(PERMISSION.PriceListManage)],
    children: [
      { path: '', component: PriceListListPageComponent, title: 'pricelist.title' },
      {
        path: 'new',
        component: PriceListEditorPageComponent,
        title: 'pricelist.new',
        canDeactivate: [unsavedChangesGuard]
      },
      {
        path: ':id',
        component: PriceListEditorPageComponent,
        title: 'pricelist.title',
        canDeactivate: [unsavedChangesGuard]
      }
    ]
  }
];
