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
