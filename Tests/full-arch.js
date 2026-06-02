import http from 'k6/http';
import { check, sleep } from 'k6';

const TOKEN = __ENV.TOKEN;

export const options = {
    vus: 10,
    duration: '30s',
};

export default function () {
    const res = http.get('http://localhost:5000/api/sentinel-service/anomalies', {
        headers: { Authorization: `Bearer ${TOKEN}` },
    });
    check(res, { 'status 200': (r) => r.status === 200 });
    sleep(0.1);
}