import { PublicId } from '@ims/models';

const PUBLIC_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isDefined<T>(value: T | null | undefined): value is T {
  return value !== null && value !== undefined;
}

export function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * True for the text form of a GUID, which is what every entity id is on the wire. Used where an id comes from outside
 * the API (a link, a value saved on the device): an old numeric id, an empty string or anything else is not one.
 */
export function isPublicId(value: unknown): value is PublicId {
  return typeof value === 'string' && PUBLIC_ID_PATTERN.test(value);
}
