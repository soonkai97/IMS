// PL3 reads: sales-order list (status, customer, warehouse, search, cursor), sales-order detail, and the price lookup GET /api/price-lists/lookup. Env: VUS (30), DURATION (2m).
import { sleep } from 'k6';
import { setupPl3, pl3Thresholds, salesReadOp } from './pl3.js';
import { summaryTo } from './scenarios.js';

export const options = {
  scenarios: { 'sales-read': { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '30'), duration: __ENV.DURATION || '2m' } },
  thresholds: pl3Thresholds(),
  setupTimeout: '600s',   // setupData() samples real ids (about 700 calls) and setupPl3() adds modules, price lists and sales orders (tests/load/pl3.js)
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupPl3(); }
export default function (d) {
  salesReadOp(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('sales-read');
