# Build on the target architecture (ARM64 on a Raspberry Pi 5).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Shared build settings enable implicit usings, nullable checks and analyzers.
COPY Directory.Build.props ./
COPY core/Cadenza.Core.csproj core/
COPY server/Cadenza.Server.csproj server/
RUN dotnet restore server/Cadenza.Server.csproj

COPY core/ core/
COPY server/ server/
RUN dotnet publish server/Cadenza.Server.csproj \
    --configuration Release \
    --no-restore \
    --self-contained false \
    -p:UseAppHost=false \
    --output /out

# Runtime only: no SDK, tests, Windows desktop, Rust engine, or source tree.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Cadenza__Database=/data/cadenza.db \
    Cadenza__MusicFolder=/data/music

# The official ASP.NET Core image's unprivileged app user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cadenza.Server.dll"]
