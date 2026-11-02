# 0012 — Four layers: Api, Application, Domain, Infrastructure
Date: 2026-11-02 · Status: Accepted · Supersedes the backend part of [0006](0006-repo-structure.md)

## Context
`pamplemousse` had three layers, with use-case logic in `DestinationService` inside the Api project. The chatbot (Sprint 5) and the Telegram bot (Sprint 9) must run the same use cases as the HTTP API, so that logic cannot live in the HTTP layer. Reference studied: the `WebApi` service of FaresMaady1/warehouse-management-api, which is Clean Architecture + CQRS with a light use of DDD.

## Decision
```
Api → Application → Domain
Infrastructure → Application (implements its interfaces)
```
- **Api:** `Controllers/`, `Contracts/` (requests), `ViewModels/` (JSON shapes), `Mappings/`, `Errors/`, `Middleware/`, plus `Env.cs` and `Program.cs`. HTTP only; references Infrastructure only to register it.
- **Application:** one folder per use case, `Destinations/Queries/<UseCase>/`, with a query, a handler and a response. `Abstractions/` holds the provider interfaces and `IQueryHandler`. `Commands/` arrive with the first writes.
- **Domain:** entities, value objects (`Money`, `GeoPoint`, `DateRange`), domain exceptions. No dependencies.
- **Infrastructure:** external providers, cache, reference data; later EF Core.
- One deployed service, no microservices.

### Choices that differ from the reference
| Topic | Reference | TravelGenie | Why |
|---|---|---|---|
| Dispatch | MediatR | Plain `IQueryHandler<TQuery, TResponse>` | MediatR 13+ is commercially licensed (free tier to verify); same structure with no dependency |
| Mapping | AutoMapper | Hand-written `ToViewModel()` | AutoMapper 15+ is commercially licensed; mapping errors become compile errors |
| Provider interfaces | Domain | Application | Weather or flight sources are not business rules; repository interfaces will stay in Domain |
| Packages in Domain | EF Core, AutoMapper | None | Enforced by a test |
| Saving | `SaveChanges` per repository | One `IUnitOfWork` (when persistence arrives) | A use case commits once |

## Consequences
- The layers are enforced by `Pamplemousse.Architecture.Tests`, so a wrong reference fails the pre-push hook and CI.
- Business rules throw `DomainException` / `NotFoundException`; `ExceptionHandlingMiddleware` turns them into Problem Details.
- A little more code per endpoint: request, query, response, view model, mapping.
- API responses did not change, so `chocomousse` needs no change. Checked by comparing 29 live requests before and after. Only one thing differs because of this change: malformed query values (`limit=abc`) still return 400 but now name the field.
- DDD pays off from Sprint 3–4, when `ScoringEngine` and the `Trip` aggregate arrive; Sprint 1 data is read-only.
