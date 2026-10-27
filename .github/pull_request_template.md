## Summary
<!-- What changed and why, in a few bullets. -->

## Tests (required for every feature)
- [ ] Unit tests for new logic (Domain / Infrastructure providers with canned JSON / service with fakes)
- [ ] Integration tests for new or changed endpoints (`Pamplemousse.Api.Tests`, WebApplicationFactory)
- [ ] Failure paths covered (source down, missing key, bad input → 400/404)

## Checks
- [ ] `dotnet format Pamplemousse.slnx --verify-no-changes` is clean
- [ ] `dotnet build` has 0 warnings / 0 errors (analyzers on)
- [ ] `dotnet test`: every test passes
- [ ] New environment variables added to `Env.cs` **and** `.env.example`
