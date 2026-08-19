# syntax=docker/dockerfile:1

# Both tags are multi-arch manifests that include linux/arm64, so this builds
# and runs natively on Apple Silicon. Do not pin platform: linux/amd64.
#
# Deliberately not the -alpine variants: those ship without ICU and fall back to
# globalization-invariant mode, where decimal.ToString("C") renders "¤42.50"
# instead of "$42.50".
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# The build context is the repository root rather than this directory: this
# project references BillsMinimalApi.Contracts, which sits beside it, and a
# build cannot reach outside its context. Every path below is therefore
# repo-relative, and docker-compose.yml names this file explicitly.
#
# Restore against the csproj files alone so the (slow) restore layer is only
# invalidated by dependency changes, not by every source edit.
COPY BillsMinimalApi.Contracts/BillsMinimalApi.Contracts.csproj BillsMinimalApi.Contracts/
COPY BillsMinimalApi/BillsMinimalApi.csproj BillsMinimalApi/
RUN dotnet restore BillsMinimalApi/BillsMinimalApi.csproj

COPY BillsMinimalApi.Contracts/ BillsMinimalApi.Contracts/
COPY BillsMinimalApi/ BillsMinimalApi/
RUN dotnet publish BillsMinimalApi/BillsMinimalApi.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BillsMinimalApi.dll"]
