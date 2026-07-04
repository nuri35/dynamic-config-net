# syntax=docker/dockerfile:1
# Multi-stage: SDK image compiles + publishes, slim aspnet runtime ships the result.
# Build context is the REPO ROOT (see docker-compose.yml) so the referenced
# DynamicConfig.Library project is reachable from here.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the csproj files first, then restore — this layer is cached and only
# reruns when a project file changes, not on every source edit.
COPY src/DynamicConfig.Library/DynamicConfig.Library.csproj src/DynamicConfig.Library/
COPY src/DynamicConfig.WebUI/DynamicConfig.WebUI.csproj src/DynamicConfig.WebUI/
RUN dotnet restore src/DynamicConfig.WebUI/DynamicConfig.WebUI.csproj

# Now the sources and publish (restore already done above).
COPY src/DynamicConfig.Library/ src/DynamicConfig.Library/
COPY src/DynamicConfig.WebUI/ src/DynamicConfig.WebUI/
RUN dotnet publish src/DynamicConfig.WebUI/DynamicConfig.WebUI.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl is only here so compose can health-check the HTTP surface; the slim
# runtime image does not ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./
# aspnet:8.0 defaults ASPNETCORE_URLS to http://+:8080 — matches the compose port map.
EXPOSE 8080
ENTRYPOINT ["dotnet", "DynamicConfig.WebUI.dll"]
