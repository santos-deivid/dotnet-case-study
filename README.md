# dotnet-case-study

Estudo de caso em **.NET 8** desenvolvido como parte do TCC *"Abordagem Integrada de Segurança para Descoberta e Comunicação de Serviços em Arquiteturas de Microsserviços"* — UFC Quixadá.

O objetivo é validar, na prática, a arquitetura de segurança proposta no TCC usando o ecossistema .NET. Um segundo estudo de caso idêntico em [Spring Boot](https://github.com/santos-deivid/spring-boot-case-study) valida a independência tecnológica da abordagem.

---

## Arquitetura

```
Cliente
  │
  ▼ HTTP + JWT
┌─────────────────────┐
│   API Gateway       │  YARP 2.3.0 — valida JWT, roteia via Consul
│   porta 5000        │
└──────────┬──────────┘
           │ mTLS
           ▼
┌─────────────────────┐     mTLS      ┌─────────────────────┐
│  SentinelService    │ ────────────▶ │   AuditService      │
│  porta 5011 (HTTPS) │  + JWT M2M    │   porta 5012 (HTTPS)│
└─────────────────────┘               └─────────────────────┘
           │ mTLS                              │ mTLS
           └──────────────┬────────────────────┘
                          ▼
                ┌──────────────────┐
                │  Consul 1.19     │  Service Registry
                │  porta 8501      │  verify_incoming: true
                └──────────────────┘

┌──────────────────────────────────┐
│  Keycloak 25.0.6  porta 8443     │  Identity Provider — OAuth 2.1 / OIDC
└──────────────────────────────────┘
```

### Componentes

| Componente        | Tecnologia            | Função                                                |
| ----------------- | --------------------- | ----------------------------------------------------- |
| API Gateway       | YARP 2.3.0            | Ponto de entrada, validação JWT, roteamento dinâmico  |
| Service Registry  | HashiCorp Consul 1.19 | Descoberta de serviços protegida por mTLS             |
| Identity Provider | Keycloak 25.0.6       | OAuth 2.1, Client Credentials Flow, emissão de JWT   |
| SentinelService   | .NET 8                | Microsserviço de anomalias, comunicação inter-serviço |
| AuditService      | .NET 8                | Microsserviço de logs, autorização por `aud` + `role` |

---

## Pré-requisitos

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Git Bash](https://git-scm.com/) (para geração de certificados no Windows)
- Entrada no arquivo `hosts` do Windows:

```
# C:\Windows\System32\drivers\etc\hosts
127.0.0.1 keycloak
```

> Essa entrada é necessária para que o issuer dos tokens JWT seja consistente entre
> o ambiente externo (browser/Insomnia) e os containers Docker.

---

## Como subir a aplicação

### 1. Gerar os certificados

> **Pré-requisito:** executar no **Git Bash** dentro de `Infrastructure/certs/`.

```bash
cd Infrastructure/certs

export MSYS_NO_PATHCONV=1

# Gerar CA
openssl genrsa -out ca.key 4096
openssl req -new -x509 -days 3650 -key ca.key -out ca.crt \
  -subj "/CN=dotnet-case-study-CA/O=DotnetCaseStudy"

# Função auxiliar para gerar certificados com SAN
gen_cert() {
  local name=$1 cn=$2
  cat > san.cnf << SANCNF
[req]
req_extensions = v3_req
distinguished_name = req_distinguished_name
[req_distinguished_name]
[v3_req]
subjectAltName = DNS:localhost,DNS:${cn},IP:127.0.0.1
SANCNF
  openssl genrsa -out ${name}.key 2048
  openssl req -new -key ${name}.key -out ${name}.csr -subj "/CN=${cn}/O=DotnetCaseStudy"
  openssl x509 -req -days 365 -in ${name}.csr -CA ca.crt -CAkey ca.key \
    -CAcreateserial -out ${name}.crt -extensions v3_req -extfile san.cnf
  openssl pkcs12 -export -out ${name}.pfx -inkey ${name}.key \
    -in ${name}.crt -certfile ca.crt -passout pass:${name}123
  rm ${name}.csr san.cnf
}

gen_cert gateway gateway
gen_cert sentinel sentinel-service
gen_cert audit audit-service
gen_cert consul consul
gen_cert keycloak keycloak
```

### 2. Subir os containers

```bash
docker compose up -d
```

Na primeira execução o Keycloak realiza um build otimizado (~1-2 min). Aguarde:

```bash
docker logs keycloak --follow
# Aguardar: "Keycloak 25.0.6 ... started"
```

### 3. Verificar que tudo está rodando

```bash
docker compose ps
```

Todos os serviços devem estar com status `running`. Acesse o Consul em `https://localhost:8501` e o Keycloak em `https://keycloak:8443` (admin / admin) para confirmar.

---

## Serviços e URLs

| Serviço         | URL externa             | Observações                     |
| --------------- | ----------------------- | --------------------------------|
| Gateway         | `http://localhost:5000` | Entrada pública                 |
| Consul          | `https://localhost:8501`| Dashboard — requer certificado  |
| Keycloak        | `https://keycloak:8443` | admin / admin                   |
| SentinelService | Interno apenas          | Acessível via gateway           |
| AuditService    | Interno apenas          | Acessível apenas por M2M        |

---

## Testes

Os testes abaixo podem ser executados em qualquer cliente HTTP — **Insomnia**, **Postman**, ou o arquivo `.http` incluído em `Edge/Gateway.Yarp/`.

> **Atenção:** o Keycloak usa HTTPS com certificado de CA própria. No Insomnia/Postman,
> desabilite a verificação SSL para `keycloak:8443`, ou importe o `Infrastructure/certs/ca.crt`
> como CA confiável.

### Teste 1 — Acesso sem token (deve retornar 401)

Confirma que o Gateway bloqueia requisições não autenticadas.

```
GET http://localhost:5000/api/sentinel-service/anomalies
```

**Resultado esperado:** `401 Unauthorized`

---

### Teste 2 — Obter token de acesso

```
POST https://keycloak:8443/realms/dotnet-case-study/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=gateway-client
&client_secret=<CLIENT_SECRET>
```

> O `client_secret` do `gateway-client` está no realm exportado em
> `Infrastructure/keycloak/dotnet-case-study-realm.json`.

**Resultado esperado:** `200 OK` com `access_token` no corpo.
> Salve o valor de `access_token` — será usado nos testes seguintes como `{{TOKEN}}`.

---

### Teste 3 — Acessar anomalias com token (deve retornar 200)

Confirma que o Gateway aceita o token, roteia via Consul para o SentinelService,
e que o SentinelService valida o token internamente (defesa em profundidade).

```
GET http://localhost:5000/api/sentinel-service/anomalies
Authorization: Bearer {{TOKEN}}
```

**Resultado esperado:** `200 OK` com lista de anomalias.

---

### Teste 4 — Acessar AuditService com token de usuário (deve retornar 401)

Confirma que o AuditService rejeita tokens não destinados a ele (`aud` inválido).
O token do `gateway-client` tem `aud: gateway-client`, mas o AuditService exige `aud: audit-service`.

```
GET http://localhost:5000/api/audit-service/audit-logs
Authorization: Bearer {{TOKEN}}
```

**Resultado esperado:** `401 Unauthorized`

---

### Teste 5 — Fluxo inter-serviço completo (deve retornar 200)

Confirma o fluxo completo de comunicação segura entre microsserviços:

1. SentinelService descobre o AuditService via Consul (mTLS)
2. SentinelService obtém token M2M do Keycloak (Client Credentials, `microservice-client`)
3. SentinelService chama AuditService via mTLS com o token M2M
4. AuditService valida `aud: audit-service` e `role: service`

```
GET http://localhost:5000/api/sentinel-service/anomalies/report
Authorization: Bearer {{TOKEN}}
```

**Resultado esperado:** `200 OK` com objeto contendo `anomalies` e `auditLogs`.

---

### Teste 6 — Acesso ao Consul sem certificado (deve falhar)

Confirma que o Consul rejeita conexões sem certificado mTLS válido.

```bash
curl -k https://localhost:8501/v1/catalog/services
```

**Resultado esperado:** falha de handshake SSL (`curl: (56) Recv failure`).

---

### Resumo dos resultados esperados

| Teste               | Endpoint                                     | Resultado           |
| ------------------- | -------------------------------------------- | ------------------- |
| 1 — Sem token       | `GET /api/sentinel-service/anomalies`        | 401                 |
| 2 — Obter token     | `POST /token` (Keycloak)                     | 200 + access_token  |
| 3 — Token válido    | `GET /api/sentinel-service/anomalies`        | 200                 |
| 4 — Audience errado | `GET /api/audit-service/audit-logs`          | 401                 |
| 5 — Inter-serviço   | `GET /api/sentinel-service/anomalies/report` | 200                 |
| 6 — Consul sem cert | `GET https://localhost:8501/...`             | Falha SSL           |

---

## Estrutura do repositório

```
dotnet-case-study/
├── Edge/
│   └── Gateway.Yarp/          # API Gateway (YARP)
├── Application/
│   ├── SentinelService/       # Microsserviço de anomalias
│   └── AuditService/          # Microsserviço de auditoria
├── Infrastructure/
│   ├── consul/                # Configuração do Consul
│   ├── keycloak/              # Realm export
│   └── certs/                 # Certificados PKI (gerados localmente, não versionados)
├── Tests/
│   ├── baseline.js            # k6 — baseline (gateway sem token)
│   └── full-arch.js           # k6 — arquitetura completa
└── docker-compose.yml
```

---

## Referência

Repositório principal do TCC (com o segundo estudo de caso em Spring Boot):
[https://github.com/santos-deivid/tcc](https://github.com/santos-deivid/tcc)