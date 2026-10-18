export const DEFAULT_PAGE_SIZE = 25;
/** Page sizes a desktop list offers (all below the API's 200 maximum). */
export const PAGE_SIZES: readonly number[] = [25, 50, 100];
export const FIRST_PAGE = 1;
/** Debounce for type-ahead search boxes (milliseconds). */
export const SEARCH_DEBOUNCE_MS = 300;
/** Characters needed before a product search starts. */
export const MIN_SEARCH_LENGTH = 2;

/** Server limits (docs: API contract) that forms enforce too. */
/** The API answers 400 to a document search shorter than this (after trimming), so the web sends none. */
export const API_MIN_SEARCH_LENGTH = 3;
export const MAX_PAGE_SIZE = 200;
export const MAX_DOCUMENT_LINES = 500;
export const MAX_QUANTITY = 1_000_000_000;
export const MAX_QUANTITY_DECIMALS = 4;
/** Largest unit cost a receipt line takes (API Limits.MaxUnitCost). */
export const MAX_UNIT_COST = 1_000_000_000;
export const MAX_USERNAME_LENGTH = 256;
export const MAX_PASSWORD_LENGTH = 128;
