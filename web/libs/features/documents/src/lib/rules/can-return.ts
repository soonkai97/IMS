import { DocumentDto, DocumentStatus, DocumentType, IssuePurpose } from '@ims/api';

/**
 * Whether a document offers "Return": a Posted sale issue (a customer return) or a Posted receipt (a supplier return) that is not
 * itself a return, with something left to return. The API lists no `return` action, so this reads what the API does send: the
 * returned and returnable quantities of the lines are there only while the Returns module is on and the caller may create returns
 * (and only on a Posted original), so their presence IS the API's answer; the permission is asked as well.
 */
export function canReturn(document: DocumentDto | undefined, mayCreateReturns: boolean): boolean {
  if (!mayCreateReturns || document === undefined || document.status !== DocumentStatus.Posted) {
    return false;
  }
  if (document.returnOf) {
    return false;
  }
  const isSale = document.type === DocumentType.Issue && document.purpose === IssuePurpose.Sale;
  if (!isSale && document.type !== DocumentType.Receipt) {
    return false;
  }
  return document.lines.some(line => (line.returnableQty ?? 0) > 0);
}
