/** Module codes as returned by GET /api/modules (see Ims.Domain ModuleCodes). The core has no code and is always on. */
export const MODULE_CODE = {
  Accounting: 'accounting',
  Purchasing: 'purchasing'
} as const;
