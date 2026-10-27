# Builds the TravelGenie API for Google Cloud Run.
# Locally and in CI the SDK is pinned to 10.0.113 by global.json. Microsoft publishes no SDK image for
# that exact version, so the build stage uses the .NET 10 SDK image and skips global.json; the runtime
# image is pinned to 10.0.13, the runtime that ships with SDK 10.0.113.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between builds.
# .editorconfig carries the analyzer configuration (deliberate exceptions, generated-code folders);
# without it the analyzers in the image would flag things the local build accepts.
COPY .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/Pamplemousse.Api/Pamplemousse.Api.csproj src/Pamplemousse.Api/
COPY src/Pamplemousse.Domain/Pamplemousse.Domain.csproj src/Pamplemousse.Domain/
COPY src/Pamplemousse.Infrastructure/Pamplemousse.Infrastructure.csproj src/Pamplemousse.Infrastructure/
COPY src/Pamplemousse.ServiceDefaults/Pamplemousse.ServiceDefaults.csproj src/Pamplemousse.ServiceDefaults/
RUN dotnet restore src/Pamplemousse.Api/Pamplemousse.Api.csproj

COPY src/ src/
RUN dotnet publish src/Pamplemousse.Api/Pamplemousse.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS runtime
WORKDIR /app
COPY --from=build /app .
# Cloud Run sends traffic to $PORT (8080 by default).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pamplemousse.Api.dll"]
