import { API_ERROR_KIND, ApiError } from '@ims/models';
import { CONFLICT_CODE } from './stale-document';

/**
 * The 409 `over_return`: posting or saving a return asks for more of an original line than can still be returned. The API's own
 * sentence names every line with its unit and what is returnable, so it is shown as it is, in one alert (not as a stale copy).
 */
export function isOverReturnError(error: ApiError | undefined): boolean {
  return (
    error?.kind === API_ERROR_KIND.Conflict && error.extensions['code'] === CONFLICT_CODE.OverReturn
  );
}
