import http from 'k6/http';
import { check, sleep } from 'k6';

// Baseline: gateway sem token → 401
// Execução: docker run --rm --network dotnet-case-study-net -v $(pwd)/Tests:/tests grafana/k6 run /tests/baseline.js

export const options = {
    vus: 10,
    duration: '30s',
};

export default function () {
    const res = http.get('http://gateway:5000/api/sentinel-service/anomalies');
    check(res, { 'status 401': (r) => r.status === 401 });
    sleep(0.1);
}