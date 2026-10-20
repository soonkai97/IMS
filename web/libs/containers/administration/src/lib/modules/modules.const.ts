import { MODULE_CODE } from '@ims/models';

/**
 * Modules that cannot be turned ON from this screen yet; turning one OFF is always allowed. The screen shows the switch, off, with the
 * reason in words, never a hidden one.
 *
 * `accounting` waits for PL1.8. While it is on, a product or partner save that does not send the four tax fields clears them (a PUT
 * replaces all fields), and the forms of today never send them (docs/pm/pilot-slice-1-plan.md risk 11). PL1.8 removes this entry when the
 * forms always send the fields.
 */
export const NOT_YET_SWITCHABLE_ON: readonly string[] = [MODULE_CODE.Accounting];
