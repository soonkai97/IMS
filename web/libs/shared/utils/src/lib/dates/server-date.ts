import { ZONE_DESIGNATOR } from '../constants';

/**
 * The API stores UTC but serialises some timestamps without a zone designator ("2026-10-09T14:07:35.04").
 * Treat those as UTC so they are not shifted by the browser's local zone.
 */
export function parseServerDate(value: string): Date {
  const hasTime = value.includes('T');
  const normalised = hasTime && !ZONE_DESIGNATOR.test(value) ? `${value}Z` : value;
  return new Date(normalised);
}

/** yyyy-MM-dd for <input type="date"> and the API's `date` query parameters. */
export function toDateOnly(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
