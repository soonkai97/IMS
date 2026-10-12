/** Business time zone for every displayed date (BR-10). */
export const BUSINESS_TIME_ZONE = 'Asia/Kuala_Lumpur';

/** Quantities show up to this many decimals, trailing zeros trimmed (docs/design/mvp-ux.md D10, API limit 4). */
export const MAX_QTY_DECIMALS = 4;
export const MONEY_DECIMALS = 2;
/** The one place the currency of the deployment is named; the catalogs read it as {currency} (D2: labels, never values). */
export const DEFAULT_CURRENCY_LABEL = 'RM';
/** A cost per base unit is a unit price: up to 4 decimals (3.3333), never fewer than money's 2 (10.00). */
export const UNIT_PRICE_DECIMALS = 4;

/** Byte order mark so Excel opens UTF-8 CSV (Chinese, Malay) correctly. */
export const CSV_BOM = String.fromCharCode(0xfeff);
export const CSV_MIME_TYPE = 'text/csv;charset=utf-8';
