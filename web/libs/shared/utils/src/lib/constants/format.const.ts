/** Business time zone for every displayed date (BR-10). */
export const BUSINESS_TIME_ZONE = 'Asia/Kuala_Lumpur';

/** Quantities show up to this many decimals, trailing zeros trimmed (docs/design/mvp-ux.md D10, API limit 4). */
export const MAX_QTY_DECIMALS = 4;
export const MONEY_DECIMALS = 2;

/** Byte order mark so Excel opens UTF-8 CSV (Chinese, Malay) correctly. */
export const CSV_BOM = String.fromCharCode(0xfeff);
export const CSV_MIME_TYPE = 'text/csv;charset=utf-8';

/** Matches an ISO timestamp that already carries a zone designator (Z or +hh:mm). */
export const ZONE_DESIGNATOR = /(Z|[+-]\d{2}:?\d{2})$/i;
