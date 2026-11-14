// PL3 sales write flow: create sales order -> submit -> (approve, about 12 %) -> create delivery from the order -> submit -> (approve) -> post. Env: VUS (15), DURATION (2m).
import { sleep } from 'k6';
import { setupPl3, pl3Thresholds, salesOrderFlow } from './pl3.js';
import { summaryTo } from './scenarios.js';

export const options = {
  scenarios: { 'sales-write': { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '15'), duration: __ENV.DURATION || '2m' } },
  thresholds: pl3Thresholds(),
  setupTimeout: '600s',   // setupData() samples real ids (about 700 calls) and setupPl3() adds modules, price lists and sales orders (tests/load/pl3.js)
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupPl3(); }
export default function (d) {
  salesOrderFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('sales-write');
