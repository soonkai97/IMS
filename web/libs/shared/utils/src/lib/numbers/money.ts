import { MONEY_DECIMALS } from '../constants';
import { roundQuantity } from './quantity';

/** Rounds to whole cents, half away from zero like the API's Money.Round: 0.005 -> 0.01 (never sub-cent). */
export function roundMoney(value: number): number {
  return roundQuantity(value, MONEY_DECIMALS);
}
