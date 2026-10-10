import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { PartnersPageComponent } from './partners/partners.component';
import { WarehousesPageComponent } from './warehouses/warehouses.component';

const canRead = requirePermission(PERMISSION.MasterDataRead);

export const WAREHOUSE_ROUTES: Routes = [
  {
    path: '',
    component: WarehousesPageComponent,
    title: 'warehouses.title',
    canActivate: [canRead]
  }
];
export const PARTNER_ROUTES: Routes = [
  { path: '', component: PartnersPageComponent, title: 'partners.title', canActivate: [canRead] }
];
