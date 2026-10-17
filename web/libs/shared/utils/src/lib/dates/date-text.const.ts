/** The earliest date a filter accepts: older dates only ever came from typos and made the API answer 500 (ER-06). */
export const MIN_FILTER_YEAR = 2000;
export const MIN_FILTER_DATE = `${MIN_FILTER_YEAR}-01-01`;
/** The date the placeholder and the "Enter a date such as ..." sentence show in the active language. */
export const DATE_EXAMPLE = '2026-10-09';
/** The names a typed month may use whatever the active language: English is always understood. */
export const FALLBACK_DATE_LOCALE = 'en-GB';
