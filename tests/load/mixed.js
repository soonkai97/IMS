// (c) Mixed 80/20 read/write by iteration. Env: VUS (50), DURATION (3m).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { mixed: { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '50'), duration: __ENV.DURATION || '3m' } },
  thresholds: THRESHOLDS,
};
export function setup() { return setupData(); }
export default function (d) {
  if (Math.random() < 0.8) readOp(d); else writeFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('mixed');
