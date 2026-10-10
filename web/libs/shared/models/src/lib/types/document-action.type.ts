import { DOCUMENT_ACTION } from '../constants';

export type DocumentAction = (typeof DOCUMENT_ACTION)[keyof typeof DOCUMENT_ACTION];

const KNOWN_ACTIONS: readonly string[] = Object.values(DOCUMENT_ACTION);

export function isDocumentAction(value: string): value is DocumentAction {
  return KNOWN_ACTIONS.includes(value);
}

/** Server `allowedActions` -> typed set. Unknown names are ignored so a newer server cannot break the UI. */
export function toDocumentActions(raw: readonly string[] | undefined): ReadonlySet<DocumentAction> {
  return new Set((raw ?? []).filter(isDocumentAction));
}
