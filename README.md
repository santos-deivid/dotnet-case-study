# dotnet-case-study

Estudo de caso em **.NET 8** desenvolvido como parte do TCC *"Abordagem Integrada de Segurança para Descoberta e Comunicação de Serviços em Arquiteturas de Microsserviços"* — UFC Quixadá.

O objetivo é validar, na prática, a arquitetura de segurança proposta no TCC usando o ecossistema .NET. Um segundo estudo de caso idêntico em Spring Boot valida a independência tecnológica da abordagem.

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
│  porta 5011 (HTTPS) │  + JWT M2M    │   porta 5012 (HTTPS) │
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

| Componente | Tecnologia | Função |
|---|---|---|
| API Gateway | YARP 2.3.0 | Ponto de entrada, validação JWT, roteamento dinâmico |
| Service Registry | HashiCorp Consul 1.19 | Descoberta de serviços protegida por mTLS |
| Identity Provider | Keycloak 25.0.6 | OAuth 2.1, Client Credentials Flow, emissão de JWT |
| SentinelService | .NET 8 | Microsserviço de anomalias, comunicação inter-serviço |
| AuditService | .NET 8 | Microsserviço de logs, autorização por `aud` + `role` |

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
  cat > san.cnf << EOF
[req]
req_extensions = v3_req
distinguished_name = req_distinguished_name
[req_distinguished_name]
[v3_req]
subjectAltName = DNS:localhost,DNS:${cn},IP:127.0.0.1
EOF
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
docker-compose up -d
```

Na primeira execução o Keycloak realiza um build otimizado (~1-2 min). Aguarde:

```bash
docker logs keycloak --follow
# Aguardar: "Keycloak 25.0.6 ... started"
```

### 3. Verificar que tudo está rodando

```bash
docker-compose ps
```

Todos os serviços devem estar com status `running`. Acesse o Keycloak em
`https://keycloak:8443` (admin / admin) para confirmar o realm `dotnet-case-study`.

---

## Testes

Os testes abaixo podem ser executados em qualquer cliente HTTP — **Insomnia**,
**Postman**, ou o arquivo `.http` incluído em `Edge/Gateway.Yarp/`.

> **Atenção:** o Keycloak usa HTTPS com certificado de CA própria. No Insomnia/Postman,
> desabilite a verificação SSL para `keycloak:8443` nas configurações do ambiente,
> ou importe o `Infrastructure/certs/ca.crt` como CA confiável.

---

### Teste 1 — Acesso sem token (deve retornar 401)

Confirma que o Gateway bloqueia requisições não autenticadas.

```http
GET http://localhost:5000/api/sentinel-service/anomalies
```

**Resultado esperado:** `401 Unauthorized`

---

### Teste 2 — Obter token de acesso

```http
POST https://keycloak:8443/realms/dotnet-case-study/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=gateway-client
&client_secret=Kgkx76P0aELWK6JCXsrbd8y9yfknOTsE
```

**Resultado esperado:** `200 OK` com `access_token` no corpo.

> Salve o valor de `access_token` — será usado nos testes seguintes como `{{TOKEN}}`.

---

### Teste 3 — Acessar anomalias com token (deve retornar 200)

Confirma que o Gateway aceita o token e roteia via Consul para o SentinelService,
e que o SentinelService valida o token internamente (defesa em profundidade).

```http
GET http://localhost:5000/api/sentinel-service/anomalies
Authorization: Bearer {{TOKEN}}
```

**Resultado esperado:** `200 OK` com lista de anomalias.

---

### Teste 4 — Acessar AuditService diretamente com token de usuário (deve retornar 401)

Confirma que o AuditService rejeita tokens não destinados a ele (`aud` inválido).
O token do `gateway-client` tem `aud: gateway-client`, mas o AuditService exige
`aud: audit-service`.

```http
GET http://localhost:5000/api/audit-service/audit-logs
Authorization: Bearer {{TOKEN}}
```

**Resultado esperado:** `401 Unauthorized` com `error_description: "The audience 'gateway-client, account' is invalid"`

---

### Teste 5 — Fluxo inter-serviço completo (deve retornar 200)

Confirma o fluxo completo de comunicação segura entre microsserviços:
1. SentinelService descobre o AuditService via Consul (mTLS)
2. SentinelService obtém token M2M do Keycloak (Client Credentials, `microservice-client`)
3. SentinelService chama AuditService via mTLS com o token M2M
4. AuditService valida `aud: audit-service` e `role: service`

```http
GET http://localhost:5000/api/sentinel-service/anomalies/report
Authorization: Bearer {{TOKEN}}
```

**Resultado esperado:** `200 OK` com objeto contendo `anomalies` e `auditLogs`.

---

### Teste 6 — Acesso direto ao Consul sem certificado (deve falhar)

Confirma que o Consul rejeita conexões sem certificado mTLS válido.

```http
GET https://localhost:8500/v1/catalog/services
```

**Resultado esperado:** falha de handshake SSL (`certificate_unknown` ou equivalente).

---

### Resumo dos resultados esperados

| Teste | Endpoint | Resultado |
|---|---|---|
| 1 — Sem token | `GET /api/sentinel-service/anomalies` | 401 |
| 2 — Obter token | `POST /token` (Keycloak) | 200 + access_token |
| 3 — Token válido | `GET /api/sentinel-service/anomalies` | 200 |
| 4 — Audience errado | `GET /api/audit-service/audit-logs` | 401 |
| 5 — Inter-serviço | `GET /api/sentinel-service/anomalies/report` | 200 |
| 6 — Consul sem cert | `GET https://localhost:8500/...` | Falha SSL |

---

## Estrutura do repositório

```
dotnet-case-study/
├── Edge/
│   └── Gateway.Yarp/          # API Gateway (YARP)
├── Application/
│   ├── SentinelService/        # Microsserviço de anomalias
│   └── AuditService/           # Microsserviço de logs de auditoria
└── Infrastructure/
    ├── consul/                 # Configuração do Consul (mTLS)
    ├── keycloak/               # Realm export do Keycloak (não versionado)
    └── certs/                  # Certificados (não versionados)
```

---

## Contexto — TCC

Este repositório é um dos dois estudos de caso do TCC. A arquitetura implementada
segue as diretrizes definidas na Seção 5.2.2 do trabalho:

- **Segurança em profundidade** — Gateway (JWT) + Consul (mTLS) + microsserviços (JWT + mTLS)
- **Centralização da identidade** — Keycloak como único IdP, OAuth 2.1 / OIDC
- **Princípio do Privilégio Mínimo** — `audience` por serviço + `role` granular por função
- **Registro seguro** — Consul com `verify_incoming: true`, porta HTTP desabilitada
- **Configuração externalizada** — secrets via variáveis de ambiente, nada no código-fonte
- **Conteinerização** — todos os componentes em Docker Compose