# car-repair-billing-service
Esse projeto faz parte do Tech Challenge do curso de Arquitetura de Soluções da FIAP

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
