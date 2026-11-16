# car-repair-billing-service
Esse projeto faz parte do Tech Challenge do curso de Arquitetura de Soluções da FIAP

## Responsabilidades e limites do microsserviço

O **Billing Service** é responsável por orçamentos, aprovação/rejeição de orçamentos, cobrança, consulta de pagamentos e compensação financeira das ordens de serviço. A criação de OS e a execução de reparos pertencem aos serviços `car-repair-os-service` e `car-repair-production-service`, respectivamente.

## Arquitetura e persistência

- **API:** controllers REST em `src/Api/Controllers` (`/api/budgets` e `/api/payments`); Swagger/OpenAPI habilitado no ambiente de desenvolvimento.
- **Application:** comandos, consultas e validações com MediatR e FluentValidation para orçamentos e pagamentos.
- **Domain:** entidades e regras de orçamento, itens, pagamento e transações de pagamento.
- **Infrastructure:** EF Core e PostgreSQL (`ApplicationDbContext`, repositórios e migrations), RabbitMQ e implementação de gateway de pagamento.
- **Banco próprio:** PostgreSQL `billing-postgres` na plataforma Kubernetes, com credenciais dedicadas; o Billing Service não consulta os bancos dos demais serviços.

A abstração `IPaymentGateway` permite selecionar `Fake` (cenários automatizados) ou `MercadoPago` (Checkout Pro, confirmação assíncrona e estorno). Os detalhes da integração estão na seção [Pagamentos com Mercado Pago](#pagamentos-com-mercado-pago).

## Participação na Saga e mensageria

A Saga é **orquestrada** pelo repositório `car-repair-saga-orchestrator`. O Billing Service consome comandos do RabbitMQ e publica eventos de domínio, sem acessar diretamente outros bancos:

| Comando recebido | Responsabilidade |
| --- | --- |
| `CreateBudgetCommand` | Criar orçamento para a OS |
| `ProcessPaymentCommand` | Processar a cobrança do orçamento aprovado |
| `CancelBudgetCommand` | Cancelar orçamento e estornar pagamentos aprovados (compensação) |

Eventos relevantes para o fluxo: `BudgetCreated`, `BudgetApproved`, `BudgetRejected`, `PaymentApproved` e `PaymentRejected`. O `correlationId` identifica a OS em todas as mensagens. Veja o [contrato de mensageria](docs/contrato-mensageria.md) para exchanges, payloads, eventos, comandos e compensações.

## Evidência de testes e cobertura — FIAP Fase 4

- **Cobertura de linhas:** **88,32%** (mínimo exigido: 80%), medida no CI da branch `main` em **15/11/2026**.
- **Evidência:** [execução bem-sucedida do GitHub Actions CI — testes, cobertura, SonarCloud e Docker](https://github.com/Mavidev-Solucoes/car-repair-billing-service/actions/runs/43690140968).
- **Testes no repositório:** `tests/UnitTests` e `tests/IntegrationTests` (incluindo fluxos de Saga e do Mercado Pago com API externa simulada).
- **Fluxo distribuído:** [E2E Kubernetes com cenários de sucesso e compensação](https://github.com/Mavidev-Solucoes/car-repair-k8s-infra/actions/runs/43935058397).

Os percentuais são evidências de uma execução identificada, não uma afirmação de cobertura permanente. A cobertura mínima de 80% é verificada automaticamente em `.github/workflows/ci.yml`.


## GitHub Actions CI - SonarCloud Secrets

To enable SonarCloud stages in the CI pipeline, configure these repository secrets:

- `SONAR_TOKEN`: SonarCloud user token with analysis permissions.
- `SONAR_PROJECT_KEY`: SonarCloud project key.
- `SONAR_ORGANIZATION`: SonarCloud organization key.

## GitHub Actions CD - GHCR Publish

The CD workflow (`.github/workflows/cd.yml`) runs only after a successful CI run (`workflow_run`) on `main` or `develop` push events.

Required secrets and permissions:

- `GITHUB_TOKEN` (automatically provided by GitHub Actions, no manual secret creation needed).
- Workflow `permissions.contents: read` to checkout the commit from the successful CI run.
- Workflow `permissions.packages: write` to publish images to GHCR.

Published image tags:

- `latest` (published only for `main`)
- `<commit-sha>` (published for `main` and `develop`, from the successful CI run commit)

## CI Quality Gates

The CI workflow validates pull requests to `main` and pushes to `main`/`develop` with these checks:

- Build must succeed.
- Unit tests must succeed.
- Coverage must be at least **80%**.
- Sonar quality gate must pass (this check runs only when SonarCloud secrets are available in the workflow context).
- Docker image build validation must succeed.

## Pagamentos com Mercado Pago

O billing cobra pelo gateway escolhido em `PaymentGateway:Provider`:

- `Fake` (padrão): aprova na hora, ou recusa quando a saga pede `simulatePaymentFailure`. É o usado nos testes e no e2e do kind.
- `MercadoPago`: integração com o [Checkout Pro](https://www.mercadopago.com.br/developers/pt/docs/checkout-pro/landing).

Com o Mercado Pago, a cobrança é assíncrona:

1. A saga envia `ProcessPaymentCommand`; o billing cria uma preferência no Mercado Pago com `external_reference` = id do pagamento e o pagamento fica `Pending`, com o link em `checkoutUrl` (`GET /api/payments/{id}`).
2. O cliente paga no link. No sandbox, use os [cartões de teste](https://www.mercadopago.com.br/developers/pt/docs/checkout-pro/additional-content/your-integrations/test/cards): titular `APRO` aprova, `OTHE` recusa.
3. O Mercado Pago chama `POST /api/payments/webhooks/mercadopago`; o billing consulta o pagamento no Mercado Pago e aprova ou recusa. Daí sai `PaymentApproved` ou `PaymentRejected` para a saga, como no gateway falso.
4. Sem URL pública para o webhook (rodando local, por exemplo), `POST /api/payments/{id}/sync` faz a mesma consulta sob demanda.

Na compensação, pagamentos aprovados são estornados pela API de refunds do Mercado Pago. `simulatePaymentFailure` continua valendo: recusa sem chamar o Mercado Pago, para demonstrar o rollback.

Configuração (o token nunca vai para o repositório):

| Chave | Onde | Valor |
|---|---|---|
| `PaymentGateway__Provider` | ConfigMap | `MercadoPago` |
| `MercadoPago__AccessToken` | Secret `billing-mercadopago`, chave `accessToken` | Access token de teste da aplicação no painel do Mercado Pago |
| `MercadoPago__WebhookSecret` | Secret `billing-mercadopago`, chave `webhookSecret` | Assinatura secreta do webhook (opcional; com ela, notificações sem `x-signature` válida são recusadas) |
| `MercadoPago__NotificationUrl` | ConfigMap | `http://<NLB do Kong>/api/payments/webhooks/mercadopago` |

```bash
kubectl -n car-repair create secret generic billing-mercadopago \
  --from-literal=accessToken='<TOKEN DE TESTE>' \
  --from-literal=webhookSecret='<ASSINATURA DO WEBHOOK>'
```

## Observabilidade (New Relic)

A imagem traz o agente .NET do New Relic com o profiler desligado; no Kubernetes o ConfigMap liga o agente (`NEW_RELIC_APP_NAME=car-repair-billing-service`) e a license vem do Secret opcional `newrelic-license`.

O trace distribuído atravessa o RabbitMQ: `MessageTracing` grava `traceparent`/`tracestate` (W3C) e os headers do New Relic em cada mensagem publicada e, no consumo, abre a transação `RabbitMQ/<tipo>` que continua o trace de quem publicou, com o atributo `correlationId` (id da OS). Detalhes e consultas NRQL no README do `car-repair-k8s-infra`, seção "Observabilidade: New Relic APM e trace distribuído".
