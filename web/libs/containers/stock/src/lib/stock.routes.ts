import { Routes } from '@angular/router';
import { requirePermission } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { LowStockPageComponent } from './low-stock/low-stock.component';
import { ProductStockPageComponent } from './product-stock/product-stock.component';
import { StockListPageComponent } from './stock-list/stock-list.component';

const canRead = requirePermission(PERMISSION.StockRead);

export const STOCK_ROUTES: Routes = [
  { path: '', component: StockListPageComponent, title: 'stock.title', canActivate: [canRead] },
  {
    path: ':productId',
    component: ProductStockPageComponent,
    title: 'stock.title',
    canActivate: [canRead]
  }
];

export const LOW_STOCK_ROUTES: Routes = [
  { path: '', component: LowStockPageComponent, title: 'lowStock.title', canActivate: [canRead] }
];
