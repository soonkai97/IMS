/** Module codes as returned by GET /api/modules (see Ims.Domain ModuleCodes). The core has no code and is always on. */
export const MODULE_CODE = {
  Purchasing: 'purchasing'
} as const;

/**
 * Codes the API may still list that this build has no screens for (the accounting module was removed from the web; the API still has it
 * until the backend step). Settings > Modules leaves them out, so nobody can switch on a module that shows nothing. Delete this constant
 * and its filter in the Modules screen when the backend removes the module.
 */
export const MODULE_CODES_NOT_IN_THIS_BUILD: readonly string[] = ['accounting'];
