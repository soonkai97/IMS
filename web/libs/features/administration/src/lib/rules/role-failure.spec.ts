import { API_ERROR_KIND, ApiError, ApiErrorKind } from '@ims/models';
import { classifyRoleFailure } from './role-failure';

const error = (
  status: number,
  detail: string,
  kind: ApiErrorKind = API_ERROR_KIND.Conflict
): ApiError => ({
  isApiError: true,
  kind,
  status,
  title: '',
  detail,
  message: detail,
  fieldErrors: {},
  extensions: {}
});
const none = { roleChangedSince: false, removing: false };

describe('classifyRoleFailure (the API gives no code, so the screen tells the 409s apart)', () => {
  it('a 409 on a role that changed since it was opened is a stale edit', () => {
    const failure = classifyRoleFailure(error(409, 'The role was changed by someone else.'), {
      ...none,
      roleChangedSince: true
    });
    expect(failure.kind).toBe('stale');
  });

  it('a 409 on an unchanged role is not stale: a locked or standard role keeps its own sentence', () => {
    const failure = classifyRoleFailure(
      error(409, 'System roles cannot be deleted or deactivated.'),
      none
    );
    expect(failure).toEqual({
      kind: 'conflict',
      message: 'System roles cannot be deleted or deactivated.'
    });
  });

  it('a 409 that counts people is "in use", with the count from the sentence', () => {
    const failure = classifyRoleFailure(
      error(409, '3 user(s) still have this role. Move them to another role first.'),
      { ...none, removing: true, knownUserCount: 9 }
    );
    expect(failure.kind).toBe('inUse');
    expect(failure.count).toBe(3);
  });

  it('falls back to the count the screen knows when the sentence has none', () => {
    const failure = classifyRoleFailure(error(409, 'Conflict.'), {
      ...none,
      removing: true,
      knownUserCount: 2
    });
    expect(failure).toEqual({ kind: 'inUse', message: 'Conflict.', count: 2 });
  });

  it('a 409 with no people (a name that is taken) is a plain conflict', () => {
    const failure = classifyRoleFailure(error(409, "A role named 'X' already exists."), none);
    expect(failure.kind).toBe('conflict');
  });

  it('403 is containment, 404 not found, 400 invalid, anything else other', () => {
    expect(classifyRoleFailure(error(403, 'x', API_ERROR_KIND.Forbidden), none).kind).toBe(
      'containment'
    );
    expect(classifyRoleFailure(error(404, 'x', API_ERROR_KIND.NotFound), none).kind).toBe(
      'notFound'
    );
    expect(classifyRoleFailure(error(400, 'x', API_ERROR_KIND.Validation), none).kind).toBe(
      'invalid'
    );
    expect(classifyRoleFailure(error(500, 'x', API_ERROR_KIND.Server), none).kind).toBe('other');
  });
});
