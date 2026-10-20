/**
 * Shape of the tax-ready fields of the accounting module: how many characters and which. Mirrors Ims.Domain `Limits` and `TaxFieldShapes`
 * (one definition on the server for the entity, the validators and the import; the spec of this file reads the C# and fails when they
 * drift). ASCII only, no checksum and no lookup: the system only carries the values to the accountant's package. The server trims the
 * ends and counts the length of the trimmed value; a blank value means none.
 */
export interface TaxFieldShape {
  readonly maxLength: number;
  /** The whole trimmed value must match. */
  readonly pattern: RegExp;
}

export const TAX_FIELD = {
  /** Product: digits and dots. */
  HsCode: { maxLength: 16, pattern: /^[0-9.]+$/ },
  /** Product: letters, digits, hyphen. */
  SstCode: { maxLength: 20, pattern: /^[0-9A-Za-z-]+$/ },
  /** Partner: letters and digits. */
  Tin: { maxLength: 20, pattern: /^[0-9A-Za-z]+$/ },
  /** Partner: letters, digits, hyphen. */
  SstRegNo: { maxLength: 30, pattern: /^[0-9A-Za-z-]+$/ }
} as const satisfies Record<string, TaxFieldShape>;
