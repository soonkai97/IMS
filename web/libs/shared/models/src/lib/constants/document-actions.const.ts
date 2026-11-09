/** Action names in the `allowedActions` array of a document. The server decides; the UI only renders. */
export const DOCUMENT_ACTION = {
  Edit: 'edit',
  Submit: 'submit',
  Approve: 'approve',
  Reject: 'reject',
  Cancel: 'cancel',
  /** The submitter takes a Submitted document back to Draft; the button reads "Edit". */
  Recall: 'recall',
  Post: 'post',
  Reverse: 'reverse',
  /** Start a return of a Posted document. The API lists no such action; the web offers it from the document itself (see canReturn). */
  Return: 'return'
} as const;

/** Smallest accepted length of a reject / reverse / override reason (docs/design/mvp-ux.md 6.4). */
export const MIN_REASON_LENGTH = 5;
