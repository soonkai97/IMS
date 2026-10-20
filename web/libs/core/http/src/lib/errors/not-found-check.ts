import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * The `detail` of the API's 404 for an address that names no endpoint (Ims.Domain `Messages.RouteNotFound`). A turned-off module's
 * endpoint answers with exactly this body, while a handler that found no record says its own sentence ("Product not found."), so
 * only this text can mean "a module was turned off". A unit test pins it to the C# constant.
 */
export const UNKNOWN_ROUTE_DETAIL = 'The requested resource was not found.';

/**
 * Asked by the error interceptor when an API call answers 404 with the body of an unknown address (`requestUrl` is the call that
 * failed). A turned-off module's endpoint answers like that, so the only way to tell is to look at the module list again. The check
 * returns the sentence to show instead of "not found" when it finds the cause (a module turned off), else nothing. Never throws.
 * Provided by the identity library, which owns the modules; none provided means "not found" as before.
 */
export type ApiNotFoundCheck = (requestUrl: string) => Observable<string | undefined>;

export const API_NOT_FOUND_CHECK = new InjectionToken<ApiNotFoundCheck | undefined>(
  'API_NOT_FOUND_CHECK',
  { providedIn: 'root', factory: () => undefined }
);
