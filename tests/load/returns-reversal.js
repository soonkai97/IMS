// PL3 returns and reversal: 50 % customer return (issue -> post -> POST /api/returns -> submit -> post), 50 % reversal (receipt -> post -> reverse). Env: VUS (10), DURATION (2m).
import { sleep } from 'k6';
import { setupPl3, pl3Thresholds, returnFlow, reversalFlow } from './pl3.js';
import { summaryTo } from './scenarios.js';

export const options = {
  scenarios: { 'returns-reversal': { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '10'), duration: __ENV.DURATION || '2m' } },
  thresholds: pl3Thresholds(),
  setupTimeout: '600s',   // setupData() samples real ids (about 700 calls) and setupPl3() adds modules, price lists and sales orders (tests/load/pl3.js)
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupPl3(); }
export default function (d) {
  if (Math.random() < 0.5) returnFlow(d); else reversalFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('returns-reversal');
