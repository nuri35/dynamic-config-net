# syntax=docker/dockerfile:1
# Sample library consumer. Same multi-stage shape as the WebUI image; build
# context is the REPO ROOT so DynamicConfig.Library is reachable.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/DynamicConfig.Library/DynamicConfig.Library.csproj src/DynamicConfig.Library/
COPY src/DynamicConfig.DemoService/DynamicConfig.DemoService.csproj src/DynamicConfig.DemoService/
RUN dotnet restore src/DynamicConfig.DemoService/DynamicConfig.DemoService.csproj

COPY src/DynamicConfig.Library/ src/DynamicConfig.Library/
COPY src/DynamicConfig.DemoService/ src/DynamicConfig.DemoService/
RUN dotnet publish src/DynamicConfig.DemoService/DynamicConfig.DemoService.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "DynamicConfig.DemoService.dll"]
