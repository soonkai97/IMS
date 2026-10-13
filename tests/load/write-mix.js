// (b) Write mix: create receipt/issue -> submit -> (approve ~5%) -> post, spread over warehouses and Zipf-skewed products. Env: VUS (20), DURATION (2m).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { writes: { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '20'), duration: __ENV.DURATION || '2m' } },
  thresholds: THRESHOLDS,
  setupTimeout: '300s',   // setup() samples real ids through the API (about 700 calls, see scenarios.js)
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupData(); }
export default function (d) {
  writeFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('write-mix');
