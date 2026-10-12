import { MAX_QTY_DECIMALS } from '../constants';

/** Rounds to the quantity precision (4 decimals, MAX_QTY_DECIMALS) so float noise like 0.1 + 0.2 never reaches the screen. */
export function roundQuantity(value: number, decimals: number = MAX_QTY_DECIMALS): number {
  const factor = 10 ** decimals;
  return Math.round((value + Number.EPSILON) * factor) / factor;
}

/** Number or numeric string -> number, empty / invalid -> 0. */
export function toNumber(value: unknown): number {
  const parsed = typeof value === 'number' ? value : Number.parseFloat(String(value ?? ''));
  return Number.isFinite(parsed) ? parsed : 0;
}
