// Concurrency burst: 50 VUs each run ONE create->submit->post flow at the same moment on the same 5 hot products in warehouse 1. Check SQL deadlock counters afterwards (expected 0).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { burst: { executor: 'per-vu-iterations', vus: 50, iterations: parseInt(__ENV.ITERATIONS || '3'), maxDuration: '2m' } },
  thresholds: THRESHOLDS,
};
export function setup() { return setupData(); }
export default function (d) {
  writeFlow(d, { hot: true });
}
export const handleSummary = summaryTo('post-burst');
