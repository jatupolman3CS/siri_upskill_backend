import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    search_burst: {
      executor: 'constant-arrival-rate',
      rate: 200, // 200 requests/sec search throughput
      timeUnit: '1s',
      duration: '2m',
      preAllocatedVUs: 150,
      maxVUs: 600,
    },
  },
  thresholds: {
    'http_req_duration': ['p(95)<150'], // 95% of searches under 150ms (OutputCache & FullText)
    'http_req_failed': ['rate<0.001'], // 99.9% success rate
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5190';
const SEARCH_TERMS = ['docker', 'react', 'csharp', 'python', 'cloud', 'devops', 'kubernetes', 'golang'];

export default function () {
  const term = SEARCH_TERMS[Math.floor(Math.random() * SEARCH_TERMS.length)];
  const page = Math.floor(Math.random() * 3) + 1;

  const params = {
    headers: {
      'User-Agent': 'k6-load-test/search-burst',
      'Accept': 'application/json',
    },
  };

  const res = http.get(`${BASE_URL}/api/catalog/courses?search=${term}&page=${page}&pageSize=12`, params);

  check(res, {
    'search status is 200': (r) => r.status === 200,
  });

  sleep(0.5);
}
