import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { DocumentType } from '@ims/api';
import { HOME_ROUTE, PermissionService, requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { NEW_DOCUMENT_ID } from '@ims/features/documents';
import { PERMISSION } from '@ims/models';
import { DocumentEditorPageComponent } from './document-editor/document-editor.component';
import { DocumentListPageComponent } from './document-list/document-list.component';

const canRead = requirePermission(PERMISSION.StockRead);
const canCreate = requirePermission(PERMISSION.DocumentCreate);

/** `/new` needs the right to create; a saved document needs the right to read stock and documents. */
const canOpenDocument: CanActivateFn = (route, state) =>
  route.paramMap.get('id') === NEW_DOCUMENT_ID ? canCreate(route, state) : canRead(route, state);

/**
 * A user who sees no warehouse (not Admin or Owner, none assigned) cannot save anything: the form would only fail
 * at save with a 403. Home explains it ("You aren't assigned to a warehouse yet"), so send them there (EA-14).
 */
export const needsWarehouseToCreate: CanActivateFn = route => {
  if (route.paramMap.get('id') !== NEW_DOCUMENT_ID) {
    return true;
  }
  const permissions = inject(PermissionService);
  return permissions.$seesAllWarehouses() || permissions.$warehouseIds().length > 0
    ? true
    : inject(Router).createUrlTree([HOME_ROUTE]);
};

/**
 * Receipts and issues share the pages; the route `data.type` (bound to the `type` input) picks the flavour. `new` and
 * a document id are ONE route, so Angular keeps the editor alive when a save moves `/new` to `/<id>`: the form, the
 * running submit/post chain and the open confirm dialog all survive (NV-01, NV-02).
 */
function documentRoutes(type: DocumentType, titleKey: string): Routes {
  return [
    {
      path: '',
      component: DocumentListPageComponent,
      title: titleKey,
      data: { type },
      canActivate: [canRead]
    },
    {
      path: ':id',
      component: DocumentEditorPageComponent,
      title: titleKey,
      data: { type },
      canActivate: [canOpenDocument, needsWarehouseToCreate],
      canDeactivate: [unsavedChangesGuard]
    }
  ];
}

export const RECEIPT_ROUTES: Routes = documentRoutes(DocumentType.Receipt, 'receipts.title');
export const ISSUE_ROUTES: Routes = documentRoutes(DocumentType.Issue, 'issues.title');
