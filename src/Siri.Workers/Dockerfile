# Multi-stage build for Siri.Workers (.NET 10 LTS Worker Service)
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy directory configuration files
COPY backend/Directory.Build.props backend/
COPY backend/Directory.Packages.props backend/
COPY backend/SiriUpSkill.sln backend/

# Copy all project files for layer caching during restore
COPY backend/src/Siri.SharedKernel/Siri.SharedKernel.csproj backend/src/Siri.SharedKernel/
COPY backend/src/Siri.Persistence/Siri.Persistence.csproj backend/src/Siri.Persistence/
COPY backend/src/Siri.Integrations.Email/Siri.Integrations.Email.csproj backend/src/Siri.Integrations.Email/
COPY backend/src/Siri.Integrations.Payment/Siri.Integrations.Payment.csproj backend/src/Siri.Integrations.Payment/
COPY backend/src/Siri.Integrations.Storage/Siri.Integrations.Storage.csproj backend/src/Siri.Integrations.Storage/
COPY backend/src/Siri.Integrations.Video/Siri.Integrations.Video.csproj backend/src/Siri.Integrations.Video/
COPY backend/src/Siri.Modules.Identity/Siri.Modules.Identity.csproj backend/src/Siri.Modules.Identity/
COPY backend/src/Siri.Modules.Catalog/Siri.Modules.Catalog.csproj backend/src/Siri.Modules.Catalog/
COPY backend/src/Siri.Modules.Media/Siri.Modules.Media.csproj backend/src/Siri.Modules.Media/
COPY backend/src/Siri.Modules.Learning/Siri.Modules.Learning.csproj backend/src/Siri.Modules.Learning/
COPY backend/src/Siri.Modules.Commerce/Siri.Modules.Commerce.csproj backend/src/Siri.Modules.Commerce/
COPY backend/src/Siri.Modules.Payout/Siri.Modules.Payout.csproj backend/src/Siri.Modules.Payout/
COPY backend/src/Siri.Modules.Cms/Siri.Modules.Cms.csproj backend/src/Siri.Modules.Cms/
COPY backend/src/Siri.Modules.Community/Siri.Modules.Community.csproj backend/src/Siri.Modules.Community/
COPY backend/src/Siri.Modules.Notification/Siri.Modules.Notification.csproj backend/src/Siri.Modules.Notification/
COPY backend/src/Siri.Modules.Analytics/Siri.Modules.Analytics.csproj backend/src/Siri.Modules.Analytics/
COPY backend/src/Siri.Workers/Siri.Workers.csproj backend/src/Siri.Workers/
COPY backend/src/Siri.Api/Siri.Api.csproj backend/src/Siri.Api/

# Restore dependencies
RUN dotnet restore backend/src/Siri.Workers/Siri.Workers.csproj

# Copy source code and publish
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/Siri.Workers/Siri.Workers.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS final
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root user for security
USER $APP_UID
ENTRYPOINT ["dotnet", "Siri.Workers.dll"]
