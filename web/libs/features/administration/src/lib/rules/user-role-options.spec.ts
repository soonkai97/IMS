import { userRoleChoice } from './user-role-options';

const QA = { code: 'QARoleA', name: 'QA role A' };
const CLERK = { code: 'Clerk', name: 'Clerk' };

describe('userRoleChoice', () => {
  it('offers exactly the roles the API lists, by name, in the API order', () => {
    const { options } = userRoleChoice([CLERK, QA], undefined);

    expect(options).toEqual([
      { value: 'Clerk', label: 'Clerk' },
      { value: 'QARoleA', label: 'QA role A' }
    ]);
  });

  it('offers nothing of its own while the API list has not loaded', () => {
    expect(userRoleChoice(undefined, undefined)).toEqual({ options: [], locked: false });
  });

  it('keeps a custom role the API lists selectable for a user who holds it', () => {
    expect(userRoleChoice([CLERK, QA], QA).locked).toBe(false);
  });

  it('shows a role the API does not list, last and locked, so a re-save keeps it', () => {
    const choice = userRoleChoice([CLERK], QA);

    expect(choice.locked).toBe(true);
    expect(choice.options).toEqual([
      { value: 'Clerk', label: 'Clerk' },
      { value: 'QARoleA', label: 'QA role A' }
    ]);
  });

  it('shows the current role alone, unlocked, while the list loads or when it failed', () => {
    expect(userRoleChoice(undefined, QA)).toEqual({
      options: [{ value: 'QARoleA', label: 'QA role A' }],
      locked: false
    });
  });
});
