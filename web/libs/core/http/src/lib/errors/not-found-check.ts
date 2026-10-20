import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Asked by the error interceptor when an API call answers 404 (`requestUrl` is the call that failed). A turned-off module's endpoint
 * answers with the same 404 as an address that does not exist, so the only way to tell is to look at the module list again. The check
 * returns the sentence to show instead of "not found" when it finds the cause (a module turned off), else nothing. Never throws.
 * Provided by the identity library, which owns the modules; none provided means "not found" as before.
 */
export type ApiNotFoundCheck = (requestUrl: string) => Observable<string | undefined>;

export const API_NOT_FOUND_CHECK = new InjectionToken<ApiNotFoundCheck | undefined>(
  'API_NOT_FOUND_CHECK',
  { providedIn: 'root', factory: () => undefined }
);
