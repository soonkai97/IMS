import { MONEY_DECIMALS } from '../constants';
import { roundQuantity } from './quantity';

/** A quantity has at most 4 decimals (the API limit), a unit cost at most 2: both become whole numbers. */
const QTY_SCALE = 10_000;
const COST_SCALE = 10 ** MONEY_DECIMALS;
/** Half of the quantity scale: adding it before the division rounds half away from zero. */
const HALF_QTY_SCALE = BigInt(QTY_SCALE / 2);

/** Rounds to whole cents, half away from zero like the API's Money.Round: 0.005 -> 0.01, -0.005 -> -0.01. */
export function roundMoney(value: number): number {
  const rounded = roundQuantity(Math.abs(value), MONEY_DECIMALS);
  return value < 0 && rounded !== 0 ? -rounded : rounded;
}

/**
 * Line amount `qty x unit cost` in money, computed like the API: in whole numbers (quantity x 10^4, cost x 10^2), then
 * rounded half away from zero to cents. A floating-point product is wrong at the half cent (2.5 x 0.87 is 2.1749999...
 * but the exact value is 2.175, which the API stores as 2.18).
 */
export function lineAmount(qty: number, unitCost: number): number {
  const scaledQty = BigInt(Math.round(qty * QTY_SCALE));
  const scaledCost = BigInt(Math.round(unitCost * COST_SCALE));
  const product = scaledQty * scaledCost;
  const magnitude = product < 0n ? -product : product;
  const cents = (magnitude + HALF_QTY_SCALE) / BigInt(QTY_SCALE);
  return Number(product < 0n && cents !== 0n ? -cents : cents) / COST_SCALE;
}
