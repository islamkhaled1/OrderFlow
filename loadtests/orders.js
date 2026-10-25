// k6 load test for OrderFlow: authenticated order reads (cache-aside path) and order creation.
//
// Usage (API must be running, Redis optional - without it reads fall back to SQL):
//   k6 run -e BASE_URL=http://localhost:8080 loadtests/orders.js
//   k6 run -e BASE_URL=http://localhost:8080 -e VUS=20 -e DURATION=30s loadtests/orders.js
//
// NOTE: register is rate limited (RateLimiting:AuthPermitPerMinute, default 10/min per client), so a single
// customer is registered in setup() and shared by all VUs. Raise the limit in config if you want more users.
import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const VUS = parseInt(__ENV.VUS || '10');
const DURATION = __ENV.DURATION || '30s';
const PRODUCT_ID = parseInt(__ENV.PRODUCT_ID || '4');

const readLatency = new Trend('order_read_ms', true);
const createLatency = new Trend('order_create_ms', true);

export const options = {
  scenarios: {
    reads: { executor: 'constant-vus', vus: VUS, duration: DURATION, exec: 'readOrder' },
    creates: { executor: 'constant-vus', vus: Math.max(1, Math.floor(VUS / 5)), duration: DURATION, exec: 'createOrder' },
  },
  thresholds: {
    'http_req_failed{scenario:reads}': ['rate<0.01'],
    order_read_ms: ['p(95)<500'],
  },
};

const json = { 'Content-Type': 'application/json' };

export function setup() {
  const email = `k6-${Date.now()}@example.com`;
  const password = 'K6_Load_Pass1!';
  const reg = http.post(`${BASE}/api/auth/register`, JSON.stringify({ email, password, name: 'k6 user' }), { headers: json });
  check(reg, { 'registered': (r) => r.status === 200 || r.status === 201 });
  const login = http.post(`${BASE}/api/auth/login`, JSON.stringify({ email, password }), { headers: json });
  const token = login.json('accessToken');
  const auth = { headers: { ...json, Authorization: `Bearer ${token}` } };

  const created = http.post(`${BASE}/api/orders`, JSON.stringify({ items: [{ productId: PRODUCT_ID, quantity: 1 }] }), {
    headers: { ...auth.headers, 'Idempotency-Key': `k6-seed-${Date.now()}` },
  });
  if (created.status !== 201) {
    throw new Error(`Seed order failed (${created.status}): product ${PRODUCT_ID} may be out of stock; set PRODUCT_ID or restock.`);
  }
  return { token, orderId: created.json('orderId') };
}

export function readOrder(data) {
  const res = http.get(`${BASE}/api/orders/${data.orderId}`, { headers: { Authorization: `Bearer ${data.token}` } });
  readLatency.add(res.timings.duration);
  check(res, { 'read 200': (r) => r.status === 200 });
}

export function createOrder(data) {
  const res = http.post(`${BASE}/api/orders`, JSON.stringify({ items: [{ productId: PRODUCT_ID, quantity: 1 }] }), {
    headers: {
      ...json,
      Authorization: `Bearer ${data.token}`,
      'Idempotency-Key': `${__VU}-${__ITER}-${Date.now()}`,
    },
  });
  createLatency.add(res.timings.duration);
  // 409 (insufficient stock) is expected once stock runs out; anything else non-201 is a failure
  check(res, { 'create 201 or stock conflict': (r) => r.status === 201 || r.status === 409 });
}
