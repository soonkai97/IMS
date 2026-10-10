// (d) Ramping profile 10 -> 50 -> 100 -> 200 VUs over ~5.5 minutes, mixed 80/20.
import { sleep } from 'k6';
import { THRESHOLDS, setupData, readOp, writeFlow, summaryTo } from './scenarios.js';

export const options = {
  scenarios: {
    ramp: {
      executor: 'ramping-vus', startVUs: 1, gracefulRampDown: '30s',
      stages: [
        { duration: '30s', target: 10 }, { duration: '30s', target: 10 },
        { duration: '45s', target: 50 }, { duration: '45s', target: 50 },
        { duration: '45s', target: 100 }, { duration: '45s', target: 100 },
        { duration: '45s', target: 200 }, { duration: '45s', target: 200 },
        { duration: '20s', target: 0 },
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
export const handleSummary = summaryTo('ramp');
