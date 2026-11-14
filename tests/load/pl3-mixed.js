// PL3 mixed: 35 % sales reads, 25 % the base reads of read-mix, 20 % sales flow, 12 % return flow, 8 % reversal flow. Env: VUS (40), DURATION (3m).
import { sleep } from 'k6';
import { setupPl3, pl3Thresholds, salesReadOp, salesOrderFlow, returnFlow, reversalFlow } from './pl3.js';
import { readOp, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { 'pl3-mixed': { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '40'), duration: __ENV.DURATION || '3m' } },
  thresholds: pl3Thresholds(),
  setupTimeout: '600s',   // setupData() samples real ids (about 700 calls) and setupPl3() adds modules, price lists and sales orders (tests/load/pl3.js)
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupPl3(); }
export default function (d) {
  const x = Math.random();
  if (x < 0.35) salesReadOp(d);
  else if (x < 0.60) readOp(d);
  else if (x < 0.80) salesOrderFlow(d);
  else if (x < 0.92) returnFlow(d);
  else reversalFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('pl3-mixed');
