# ADR-002: Fly.io as Backend Hosting Provider

## Status

Accepted (supersedes initial Railway selection)

## Context

The ASP.NET Core backend needs a hosting platform that supports Docker containers and can maintain a persistent background service (the OpenSky polling loop). The platform must also allow outbound HTTP requests to OpenSky's API without being IP-blocked.

I evaluated three options in sequence:

**Railway**

- Simple GitHub-based deployment with a clean dashboard
- Supports Docker via explicit Dockerfile configuration
- Runs on Google Cloud Platform infrastructure
- Hobby plan at ~$5/month

**Azure Container Apps**

- Native .NET support with a consumption-based free tier
- Runs on Azure (a major cloud provider)
- More complex setup with Azure-specific tooling

**Fly.io**

- Runs on its own hardware infrastructure, not a hyperscaler
- Docker-native with CLI-based deployment
- Usage-based pricing, ~$5/month for a single small VM
- Supports persistent background processes

## Decision

I chose **Fly.io** after Railway failed in production.

The backend deployed and built successfully on Railway, but the OpenSky polling service hung indefinitely on every token request. The root cause was OpenSky blocking GCP IP ranges, which Railway runs on. The service would timeout after 100 seconds and retry in a loop without ever successfully fetching data.

Fly.io operates on its own infrastructure outside the major cloud providers. After deploying the same Docker image to Fly.io, OpenSky responded in under 30ms.

## Consequences

### Positive

- OpenSky API responds reliably from Fly.io's infrastructure
- Deployment via GitHub Actions provides proper CI/CD
- Frankfurt region (`fra`) provides low latency to OpenSky's European servers
- Machine auto-stop/start keeps costs minimal when no users are connected

### Negative

- Fly.io requires a credit card on file, even for minimal usage
- CLI-driven workflow is less visual than Railway's dashboard
- No true free tier — minimum cost is ~$3-5/month
- Corporate network restrictions (TLS inspection) can block local `fly deploy`, requiring CI/CD as the deployment path

### Lessons Learned

- Infrastructure selection is a data source compatibility concern, not just a cost or convenience decision
- Always validate that external API dependencies work from the deployment environment before committing to a platform
