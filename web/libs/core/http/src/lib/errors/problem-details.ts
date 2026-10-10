import { HttpErrorResponse } from '@angular/common/http';
import { API_ERROR_KIND, ApiError, ApiErrorKind, HTTP_STATUS } from '@ims/models';
import { isNonEmptyString, isRecord } from '@ims/utils';

const NETWORK_STATUS = 0;
const FIRST_SERVER_ERROR = 500;

function kindOf(status: number): ApiErrorKind {
  if (status === NETWORK_STATUS) {
    return API_ERROR_KIND.Network;
  }
  if (status === HTTP_STATUS.BadRequest) {
    return API_ERROR_KIND.Validation;
  }
  if (status === HTTP_STATUS.Unauthorized) {
    return API_ERROR_KIND.Unauthorized;
  }
  if (status === HTTP_STATUS.Forbidden) {
    return API_ERROR_KIND.Forbidden;
  }
  if (status === HTTP_STATUS.NotFound) {
    return API_ERROR_KIND.NotFound;
  }
  if (status === HTTP_STATUS.Conflict) {
    return API_ERROR_KIND.Conflict;
  }
  return status >= FIRST_SERVER_ERROR ? API_ERROR_KIND.Server : API_ERROR_KIND.Unknown;
}

/** The server sends camelCase paths without a command prefix ("reason", "lines[0].qty"); keep them, lower-casing the first letter. */
export function toFieldName(serverKey: string): string {
  return serverKey.charAt(0).toLowerCase() + serverKey.slice(1);
}

function toFieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) {
    return {};
  }
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      result[toFieldName(key)] = messages.filter(isNonEmptyString);
    }
  }
  return result;
}

const KNOWN_MEMBERS = new Set([
  'type',
  'title',
  'status',
  'detail',
  'instance',
  'errors',
  'traceId',
  'correlationId'
]);

/** RFC 9457 ProblemDetails body (parsed JSON or undefined) -> ApiError. */
export function problemToApiError(status: number, body: unknown): ApiError {
  const problem = isRecord(body) ? body : {};
  const extensions = Object.fromEntries(
    Object.entries(problem).filter(([key]) => !KNOWN_MEMBERS.has(key))
  );
  return {
    isApiError: true,
    kind: kindOf(status),
    status,
    title: isNonEmptyString(problem['title']) ? problem['title'] : '',
    detail: isNonEmptyString(problem['detail']) ? problem['detail'] : '',
    correlationId: isNonEmptyString(problem['correlationId'])
      ? problem['correlationId']
      : undefined,
    fieldErrors: toFieldErrors(problem['errors']),
    extensions
  };
}

/** Blob.text() where available, FileReader otherwise (older browsers, jsdom). */
function blobText(blob: Blob): Promise<string> {
  if (typeof blob.text === 'function') {
    return blob.text();
  }
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}

async function errorText(raw: unknown): Promise<string | undefined> {
  if (raw instanceof Blob) {
    return blobText(raw);
  }
  return typeof raw === 'string' ? raw : undefined;
}

/** The generated clients ask for blobs, so an error body arrives as a Blob. */
export async function readErrorBody(response: HttpErrorResponse): Promise<unknown> {
  const raw: unknown = response.error;
  const text = await errorText(raw);
  if (text === undefined) {
    return raw;
  }
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
