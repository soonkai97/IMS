import { ROLE } from '@ims/models';

export interface HeldDocument {
  /** The approval rule that fired needs a role above Supervisor. */
  readonly isLarge: boolean;
  readonly submittedById: number | undefined;
  /**
   * Set by the API (list items): the caller may approve or reject this document now. When present it IS the answer;
   * the role rules below only stand in for an API that does not send it yet.
   */
  readonly canAct?: boolean;
}

export interface Reviewer {
  readonly userId: number | undefined;
  readonly role: string | undefined;
  /** The "allow self approval" system setting. */
  readonly allowSelfApproval: boolean;
}

/**
 * Which inbox tab a held document belongs to. Mirrors the rule behind `approvalsWaiting` of GET /api/documents/counts
 * (own submissions only when self-approval is on; the Owner takes large documents, a Supervisor the others, an Admin
 * all) so the tab and the menu badge agree. The server stays the authority: the document itself only offers Approve
 * when `allowedActions` has it.
 */
export function isWaitingForReviewer(document: HeldDocument, reviewer: Reviewer): boolean {
  if (typeof document.canAct === 'boolean') {
    return document.canAct;
  }
  if (document.submittedById === reviewer.userId && !reviewer.allowSelfApproval) {
    return false;
  }
  switch (reviewer.role) {
    case ROLE.Admin:
      return true;
    case ROLE.Owner:
      return document.isLarge;
    case ROLE.Supervisor:
      return !document.isLarge;
    default:
      return false;
  }
}
