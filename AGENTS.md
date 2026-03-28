# AGENTS.md

## Project Overview
This repository will host **CPBL Line Bot Cloud**, a cloud-based CPBL notification bot built with **ASP.NET Core (.NET 8)** and **Microsoft Azure**.

The project goal is to build something that feels practical and maintainable, not over-engineered. The style should resemble an infra/system engineer's internal-tool project rather than a flashy template-generated demo.

## Product Direction
This is **not** a generic baseball score website.

It is a **LINE group notification system** focused on:
- daily CPBL schedules
- Fubon Guardians updates
- baseball news summaries
- group subscription settings
- scheduled pushes and sync jobs
- Azure deployment and observability

Primary real-world use case:
- a family LINE group that likes baseball and wants automated updates

## Engineering Style Preferences
Match these preferences as much as possible:
- Keep naming direct and readable.
- Prefer practical structure over excessive abstraction.
- Avoid unnecessary DDD/CQRS/event-sourcing complexity.
- Use comments and section markers when they improve readability.
- Keep controllers/pages thin, move reusable logic into services.
- Favor code that looks like a real engineer will maintain it.
- Do not make the project look like a purely AI-generated scaffolding dump.

## Preferred Architecture
Use a structure close to this unless there is a strong reason to adjust:

- `Data/`
- `Models/`
- `Services/`
- `Pages/` or `Controllers/`
- `Functions/` for Azure Functions jobs
- `wwwroot/`

Do not force an overly academic Clean Architecture layout in phase 1.

## Tech Stack
- .NET 8
- ASP.NET Core Razor Pages preferred for admin UI
- Entity Framework Core
- Azure App Service
- Azure Functions
- Azure SQL Database
- Azure Key Vault
- Application Insights
- GitHub Actions

## Functional Scope for MVP
1. LINE Bot webhook endpoint
2. Daily schedule query/reply
3. Team status query/reply (focus on Fubon Guardians first)
4. Baseball news sync and storage
5. Group subscription settings in admin UI
6. Push logs / sync logs in admin UI
7. Azure-ready configuration

## Non-Goals for MVP
- No complex SPA frontend
- No AKS / Kubernetes
- No microservice split unless clearly justified
- No advanced ML features in phase 1
- No complicated auth model in the first iteration

## Coding Rules
- Use nullable-aware C#.
- Use `async/await` for I/O operations.
- Add logging around sync jobs, push jobs, and webhook handling.
- Keep service names explicit, e.g. `CpblGameSyncService`, `BaseballNewsSyncService`, `LinePushService`.
- Favor simple view models and practical EF Core entities.
- Use configuration classes/options when it helps clarity.
- Add at least basic failure handling for external API calls.

## Program.cs Style
Preserve a style similar to the reference project:
- section markers like `Pre-Build`, `Add Service Area`, `Razor Build`
- direct service registrations
- practical readability over ultra-minimal startup code

## Expected First Deliverables
Codex should help create:
- solution/project skeleton
- models and DbContext
- initial Razor Pages admin screens
- webhook endpoint
- sync services interfaces + placeholder implementations
- initial EF migrations
- README and architecture docs

## Commands
Typical commands:
- `dotnet restore`
- `dotnet build`
- `dotnet run`
- `dotnet ef migrations add InitialCreate`
- `dotnet ef database update`

## Important Constraint
When making design decisions, prefer something that Ian can plausibly explain, maintain, and demo in an interview.
