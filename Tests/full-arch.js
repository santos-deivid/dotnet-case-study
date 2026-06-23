import http from 'k6/http';
import { check, sleep } from 'k6';

// Arquitetura completa: gateway com token válido → 200
// Execução: docker run --rm --network dotnet-case-study-net -v $(pwd)/Tests:/tests -e TOKEN=<token> grafana/k6 run /tests/full-arch.js

const TOKEN = __ENV.TOKEN;

export const options = {
    vus: 10,
    duration: '30s',
};

export default function () {
    const res = http.get('http://gateway:5000/api/sentinel-service/anomalies', {
        headers: { Authorization: `Bearer ${TOKEN}` },
    });
    check(res, { 'status 200': (r) => r.status === 200 });
    sleep(0.1);
}