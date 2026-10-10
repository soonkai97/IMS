/**
 * yyyy-MM-dd for <input type="date"> and the API's `date` query parameters. Every timestamp the API returns is UTC with
 * a trailing "Z", so `new Date(value)` is correct as is; only these date-only filter values need formatting.
 */
export function toDateOnly(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
