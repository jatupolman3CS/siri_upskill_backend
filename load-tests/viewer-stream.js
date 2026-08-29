import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    viewer_stream: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '1m', target: 500 },
        { duration: '2m', target: 2500 },
        { duration: '3m', target: 5000 }, // Peak 5,000 concurrent viewers
        { duration: '4m', target: 5000 },
        { duration: '2m', target: 0 },
      ],
    },
  },
  thresholds: {
    'http_req_duration{type:read}': ['p(95)<200'], // Reads under 200ms
    'http_req_duration{type:heartbeat}': ['p(95)<150'], // Heartbeat under 150ms
    'http_req_failed': ['rate<0.005'], // Error rate < 0.5%
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5190';

export default function () {
  const params = {
    headers: {
      'Content-Type': 'application/json',
      'User-Agent': 'k6-load-test/viewer-stream',
    },
  };

  // 1. Fetch category tree (cached read)
  const catRes = http.get(`${BASE_URL}/api/catalog/categories`, Object.assign({}, params, {
    tags: { type: 'read' },
  }));
  check(catRes, {
    'categories status is 200': (r) => r.status === 200,
  });

  // 2. Fetch course details
  const courseRes = http.get(`${BASE_URL}/api/catalog/courses?pageSize=10`, Object.assign({}, params, {
    tags: { type: 'read' },
  }));
  check(courseRes, {
    'courses list status is 200': (r) => r.status === 200,
  });

  // 3. Simulate video playback heartbeat every 30 seconds
  sleep(5);

  const healthRes = http.get(`${BASE_URL}/health`, Object.assign({}, params, {
    tags: { type: 'heartbeat' },
  }));
  check(healthRes, {
    'health check status is 200': (r) => r.status === 200,
  });

  sleep(25);
}
