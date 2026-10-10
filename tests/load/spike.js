// (e) Spike: 10 VUs, jump to 300 within 10 s, hold 60 s, back to 10 and watch recovery (mixed 80/20).
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: {
    spike: {
      executor: 'ramping-vus', startVUs: 10, gracefulRampDown: '30s',
      stages: [
        { duration: '30s', target: 10 }, { duration: '10s', target: 300 }, { duration: '60s', target: 300 },
        { duration: '10s', target: 10 }, { duration: '60s', target: 10 },
      ],
    },
  },
  thresholds: THRESHOLDS,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};
export function setup() { return setupData(); }
export default function (d) {
  if (Math.random() < 0.8) readOp(d); else writeFlow(d);
  sleep(Math.random() * 0.3);
}
export const handleSummary = summaryTo('spike');
