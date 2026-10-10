export const IMPORT_KIND = {
  Products: 'products',
  OpeningStock: 'opening-stock'
} as const;

export type ImportKind = (typeof IMPORT_KIND)[keyof typeof IMPORT_KIND];

export const MAX_IMPORT_BYTES = 5 * 1024 * 1024;
export const IMPORT_EXTENSION = '.xlsx';
