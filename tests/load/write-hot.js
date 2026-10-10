// (b2) Hot-product write variant: all flows hit the same 5 hot products in warehouse 1 (balance row + document sequence contention). Env: VUS (30), DURATION (2m).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { hot: { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '30'), duration: __ENV.DURATION || '2m' } },
  thresholds: THRESHOLDS,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupData(); }
export default function (d) {
  writeFlow(d, { hot: true });
}
export const handleSummary = summaryTo('write-hot');
