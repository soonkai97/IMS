// (d2) Soak: 50 VUs mixed 80/20 for 10 minutes (tokens last 15 min, so keep DURATION <= 12m). Env: VUS (50), DURATION (10m).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { soak: { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '50'), duration: __ENV.DURATION || '10m' } },
  thresholds: THRESHOLDS,
  setupTimeout: '300s',   // setup() samples real ids through the API (about 700 calls, see scenarios.js)
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupData(); }
export default function (d) {
  if (Math.random() < 0.8) readOp(d); else writeFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('soak');
