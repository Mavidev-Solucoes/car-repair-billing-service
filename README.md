# car-repair-billing-service
Esse projeto faz parte do Tech Challenge do curso de Arquitetura de Soluções da FIAP

## GitHub Actions CI - SonarCloud Secrets

To enable SonarCloud stages in the CI pipeline, configure these repository secrets:

- `SONAR_TOKEN`: SonarCloud user token with analysis permissions.
- `SONAR_PROJECT_KEY`: SonarCloud project key.
- `SONAR_ORGANIZATION`: SonarCloud organization key.

## CI Quality Gates

The CI workflow validates pull requests to `main` and pushes to `main`/`develop` with these enforced checks:

- Build must succeed.
- Unit tests must succeed.
- Coverage must be at least **80%**.
- Sonar quality gate must pass (when SonarCloud secrets are available).
- Docker image build validation must succeed.
