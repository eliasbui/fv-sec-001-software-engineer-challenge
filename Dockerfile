# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy sln and project files first for layer caching
COPY AdPerformance.sln .
COPY src/AdPerformance.Core/AdPerformance.Core.csproj src/AdPerformance.Core/
COPY src/AdPerformance.Infrastructure/AdPerformance.Infrastructure.csproj src/AdPerformance.Infrastructure/
COPY src/AdPerformance.CLI/AdPerformance.CLI.csproj src/AdPerformance.CLI/

RUN dotnet restore src/AdPerformance.CLI/AdPerformance.CLI.csproj

# Copy source and build
COPY src/ src/
RUN dotnet publish src/AdPerformance.CLI/AdPerformance.CLI.csproj \
    -c Release \
    -r linux-arm64 \
    -p:PublishSingleFile=true \
    -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/AdPerformance /usr/local/bin/AdPerformance

ENTRYPOINT ["AdPerformance"]