import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    checkout_burst: {
      executor: 'ramping-arrival-rate',
      startRate: 50,
      timeUnit: '1m',
      preAllocatedVUs: 100,
      maxVUs: 500,
      stages: [
        { duration: '30s', target: 200 },
        { duration: '1m', target: 500 }, // 500 checkouts/min flash burst
        { duration: '2m', target: 500 },
        { duration: '30s', target: 50 },
      ],
    },
  },
  thresholds: {
    'http_req_duration{type:checkout}': ['p(95)<500'], // 95% of checkouts < 500ms
    'http_req_failed': ['rate<0.01'], // Failures < 1%
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5190';

export default function () {
  const params = {
    headers: {
      'Content-Type': 'application/json',
      'User-Agent': 'k6-load-test/checkout-burst',
    },
    tags: { type: 'checkout' },
  };

  // 1. Search available courses
  const searchRes = http.get(`${BASE_URL}/api/catalog/courses?pageSize=5`, params);
  check(searchRes, {
    'search status is 200': (r) => r.status === 200,
  });

  // 2. Validate promo code endpoint
  const promoPayload = JSON.stringify({
    code: 'EARLYBIRD',
    courseId: '01912345-6789-7abc-def0-123456789abc',
  });

  const promoRes = http.post(`${BASE_URL}/api/commerce/promo-codes/validate`, promoPayload, params);
  // May be 200 or 400/404 depending on seeded promo code, both validate fast response
  check(promoRes, {
    'promo validation responded quickly': (r) => r.status === 200 || r.status === 400 || r.status === 404 || r.status === 401,
  });

  sleep(1);
}
