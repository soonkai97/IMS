// (a) Read mix: stock enquiry (filters), ledger by product, documents list/by id, products search, low-stock, summary. Env: VUS (50), DURATION (2m).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: { reads: { executor: 'constant-vus', vus: parseInt(__ENV.VUS || '50'), duration: __ENV.DURATION || '2m' } },
  thresholds: THRESHOLDS,
  noConnectionReuse: false,
};
export function setup() { return setupData(); }
export default function (d) {
  readOp(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('read-mix');
