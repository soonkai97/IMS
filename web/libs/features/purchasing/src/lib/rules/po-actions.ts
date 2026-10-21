import { PO_ACTION, PoAction } from '../constants/purchasing.const';

const KNOWN_ACTIONS: readonly string[] = Object.values(PO_ACTION);

/** Server `allowedActions` -> typed set. A name this web does not know (a newer API) is ignored, never guessed. */
export function toPoActions(raw: readonly string[] | undefined): ReadonlySet<PoAction> {
  return new Set((raw ?? []).filter((name): name is PoAction => KNOWN_ACTIONS.includes(name)));
}
