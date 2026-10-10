import { ROLE } from '@ims/models';
import { HeldDocument, Reviewer, isWaitingForReviewer } from './approval-visibility';

const small: HeldDocument = { isLarge: false, submittedById: 1 };
const large: HeldDocument = { isLarge: true, submittedById: 1 };

function reviewer(role: string, overrides: Partial<Reviewer> = {}): Reviewer {
  return { userId: 9, role, allowSelfApproval: false, ...overrides };
}

describe('isWaitingForReviewer', () => {
  it('gives a Supervisor the documents that are not large, an Owner the large ones, an Admin all', () => {
    expect(isWaitingForReviewer(small, reviewer(ROLE.Supervisor))).toBe(true);
    expect(isWaitingForReviewer(large, reviewer(ROLE.Supervisor))).toBe(false);
    expect(isWaitingForReviewer(large, reviewer(ROLE.Owner))).toBe(true);
    expect(isWaitingForReviewer(small, reviewer(ROLE.Owner))).toBe(false);
    expect(isWaitingForReviewer(small, reviewer(ROLE.Admin))).toBe(true);
    expect(isWaitingForReviewer(large, reviewer(ROLE.Admin))).toBe(true);
  });

  it('gives roles that cannot approve nothing', () => {
    expect(isWaitingForReviewer(small, reviewer(ROLE.WarehouseStaff))).toBe(false);
    expect(isWaitingForReviewer(small, reviewer(ROLE.Clerk))).toBe(false);
    expect(isWaitingForReviewer(small, reviewer(ROLE.Admin, { role: undefined }))).toBe(false);
  });

  it('hides own submissions unless self-approval is on', () => {
    const own: HeldDocument = { isLarge: false, submittedById: 9 };

    expect(isWaitingForReviewer(own, reviewer(ROLE.Supervisor))).toBe(false);
    expect(isWaitingForReviewer(own, reviewer(ROLE.Supervisor, { allowSelfApproval: true }))).toBe(
      true
    );
  });
});
