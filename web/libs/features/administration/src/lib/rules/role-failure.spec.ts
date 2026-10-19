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
const none = { roleChangedSince: false };

describe('classifyRoleFailure (the API gives no code, so the screen tells the 409s apart)', () => {
  it('a 409 on a role that changed since it was opened is a stale edit', () => {
    const failure = classifyRoleFailure(error(409, 'The role was changed by someone else.'), {
      ...none,
      roleChangedSince: true
    });
    expect(failure.kind).toBe('stale');
  });

  it('403 is containment, 404 not found, 400 invalid, anything else other', () => {
    const forbidden = classifyRoleFailure(
      error(403, 'Own sentence.', API_ERROR_KIND.Forbidden),
      none
    );
    expect(forbidden).toEqual({ kind: 'containment', message: 'Own sentence.' });
    expect(classifyRoleFailure(error(404, 'x', API_ERROR_KIND.NotFound), none).kind).toBe(
      'notFound'
    );
    expect(classifyRoleFailure(error(400, 'x', API_ERROR_KIND.Validation), none).kind).toBe(
      'invalid'
    );
    expect(classifyRoleFailure(error(500, 'x', API_ERROR_KIND.Server), none).kind).toBe('other');
  });
});
