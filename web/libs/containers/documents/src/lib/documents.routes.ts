import { Routes } from '@angular/router';
import { DocumentType } from '@ims/api';
import { requirePermission } from '@ims/core/identity';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { PERMISSION } from '@ims/models';
import { DocumentEditorPageComponent } from './document-editor/document-editor.component';
import { DocumentListPageComponent } from './document-list/document-list.component';

const canRead = requirePermission(PERMISSION.StockRead);
const canCreate = requirePermission(PERMISSION.DocumentCreate);

/** Receipts and issues share the pages; the route `data.type` (bound to the `type` input) picks the flavour. */
function documentRoutes(type: DocumentType): Routes {
  return [
    { path: '', component: DocumentListPageComponent, data: { type }, canActivate: [canRead] },
    {
      path: 'new',
      component: DocumentEditorPageComponent,
      data: { type },
      canActivate: [canCreate],
      canDeactivate: [unsavedChangesGuard]
    },
    {
      path: ':id',
      component: DocumentEditorPageComponent,
      data: { type },
      canActivate: [canRead],
      canDeactivate: [unsavedChangesGuard]
    }
  ];
}

export const RECEIPT_ROUTES: Routes = documentRoutes(DocumentType.Receipt);
export const ISSUE_ROUTES: Routes = documentRoutes(DocumentType.Issue);
