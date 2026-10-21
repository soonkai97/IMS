import { Routes } from '@angular/router';
import { authGuard } from '@ims/core/identity';
import { ShellComponent } from '@ims/core/navigation';

/** Every feature area is a lazy chunk; each container library guards its own routes by permission. */
export const APP_ROUTES: Routes = [
  {
    path: 'login',
    loadChildren: () =>
      import('@ims/containers/authentication').then(module => module.AUTHENTICATION_ROUTES)
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'home.title',
        loadComponent: () => import('@ims/containers/home').then(module => module.HomePageComponent)
      },
      {
        path: 'receipts',
        loadChildren: () =>
          import('@ims/containers/documents').then(module => module.RECEIPT_ROUTES)
      },
      {
        path: 'issues',
        loadChildren: () => import('@ims/containers/documents').then(module => module.ISSUE_ROUTES)
      },
      {
        path: 'purchase-orders',
        loadChildren: () =>
          import('@ims/containers/purchasing').then(module => module.PURCHASE_ORDER_ROUTES)
      },
      {
        path: 'approvals',
        loadChildren: () =>
          import('@ims/containers/approvals').then(module => module.APPROVAL_ROUTES)
      },
      {
        path: 'stock',
        loadChildren: () => import('@ims/containers/stock').then(module => module.STOCK_ROUTES)
      },
      {
        path: 'low-stock',
        loadChildren: () => import('@ims/containers/stock').then(module => module.LOW_STOCK_ROUTES)
      },
      {
        path: 'products',
        loadChildren: () => import('@ims/containers/catalog').then(module => module.PRODUCT_ROUTES)
      },
      {
        path: 'catalog',
        loadChildren: () => import('@ims/containers/catalog').then(module => module.CATALOG_ROUTES)
      },
      {
        path: 'warehouses',
        loadChildren: () =>
          import('@ims/containers/warehousing').then(module => module.WAREHOUSE_ROUTES)
      },
      {
        path: 'partners',
        loadChildren: () =>
          import('@ims/containers/warehousing').then(module => module.PARTNER_ROUTES)
      },
      {
        path: 'reports',
        loadChildren: () => import('@ims/containers/reports').then(module => module.REPORT_ROUTES)
      },
      {
        path: 'users',
        loadChildren: () =>
          import('@ims/containers/administration').then(module => module.USER_ROUTES)
      },
      {
        path: 'roles',
        loadChildren: () =>
          import('@ims/containers/administration').then(module => module.ROLE_ROUTES)
      },
      {
        path: 'settings',
        loadChildren: () =>
          import('@ims/containers/administration').then(module => module.SETTINGS_ROUTES)
      },
      {
        path: 'audit-log',
        loadChildren: () => import('@ims/containers/audit').then(module => module.AUDIT_ROUTES)
      },
      {
        path: 'account',
        loadChildren: () =>
          import('@ims/containers/authentication').then(module => module.ACCOUNT_ROUTES)
      },
      {
        path: 'forbidden',
        title: 'forbidden.title',
        loadComponent: () =>
          import('@ims/containers/system').then(module => module.ForbiddenPageComponent)
      },
      {
        path: '**',
        title: 'notFound.title',
        loadComponent: () =>
          import('@ims/containers/system').then(module => module.NotFoundPageComponent)
      }
    ]
  }
];
