import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { PERMISSION } from '@ims/models';
import { CatalogListsPageComponent } from './catalog-lists/catalog-lists.component';
import { ProductEditPageComponent } from './product-edit/product-edit.component';
import { ProductImportPageComponent } from './product-import/product-import.component';
import { ProductsPageComponent } from './products/products.component';

const canRead = requirePermission(PERMISSION.MasterDataRead);
const canManage = requirePermission(PERMISSION.MasterDataManage);

/** /products/** (the "Categories & units" page has its own entry below). */
export const PRODUCT_ROUTES: Routes = [
  { path: '', component: ProductsPageComponent, canActivate: [canRead] },
  {
    path: 'new',
    component: ProductEditPageComponent,
    canActivate: [canManage],
    canDeactivate: [unsavedChangesGuard]
  },
  { path: 'import', component: ProductImportPageComponent, canActivate: [canManage] },
  {
    path: ':id',
    component: ProductEditPageComponent,
    canActivate: [canRead],
    canDeactivate: [unsavedChangesGuard]
  }
];

/** /catalog */
export const CATALOG_ROUTES: Routes = [
  { path: '', component: CatalogListsPageComponent, canActivate: [canRead] }
];
