# Multi-stage build for Siri.Api (.NET 10 LTS ASP.NET Core Web API)
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy directory configuration files
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY SiriUpSkill.sln ./

# Copy all project files for layer caching during restore
COPY src/Siri.SharedKernel/Siri.SharedKernel.csproj src/Siri.SharedKernel/
COPY src/Siri.Persistence/Siri.Persistence.csproj src/Siri.Persistence/
COPY src/Siri.Integrations.Email/Siri.Integrations.Email.csproj src/Siri.Integrations.Email/
COPY src/Siri.Integrations.Payment/Siri.Integrations.Payment.csproj src/Siri.Integrations.Payment/
COPY src/Siri.Integrations.Storage/Siri.Integrations.Storage.csproj src/Siri.Integrations.Storage/
COPY src/Siri.Integrations.Video/Siri.Integrations.Video.csproj src/Siri.Integrations.Video/
COPY src/Siri.Modules.Identity/Siri.Modules.Identity.csproj src/Siri.Modules.Identity/
COPY src/Siri.Modules.Catalog/Siri.Modules.Catalog.csproj src/Siri.Modules.Catalog/
COPY src/Siri.Modules.Media/Siri.Modules.Media.csproj src/Siri.Modules.Media/
COPY src/Siri.Modules.Learning/Siri.Modules.Learning.csproj src/Siri.Modules.Learning/
COPY src/Siri.Modules.Commerce/Siri.Modules.Commerce.csproj src/Siri.Modules.Commerce/
COPY src/Siri.Modules.Payout/Siri.Modules.Payout.csproj src/Siri.Modules.Payout/
COPY src/Siri.Modules.Cms/Siri.Modules.Cms.csproj src/Siri.Modules.Cms/
COPY src/Siri.Modules.Community/Siri.Modules.Community.csproj src/Siri.Modules.Community/
COPY src/Siri.Modules.Notification/Siri.Modules.Notification.csproj src/Siri.Modules.Notification/
COPY src/Siri.Modules.Analytics/Siri.Modules.Analytics.csproj src/Siri.Modules.Analytics/
COPY src/Siri.Workers/Siri.Workers.csproj src/Siri.Workers/
COPY src/Siri.Api/Siri.Api.csproj src/Siri.Api/

# Restore dependencies
RUN dotnet restore src/Siri.Api/Siri.Api.csproj

# Copy source code and publish
COPY src/ src/
RUN dotnet publish src/Siri.Api/Siri.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

# Expose HTTP port
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

# Run as non-root user for security
USER $APP_UID
ENTRYPOINT ["dotnet", "Siri.Api.dll"]
