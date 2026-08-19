# syntax=docker/dockerfile:1

# See BillsMinimalApi/Dockerfile for why these tags (arm64-native, and ICU is
# present so ToString("C") renders "$" rather than the invariant "¤").
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Repo-root build context, for the same reason as the API's: both projects
# reference BillsMinimalApi.Contracts. See BillsMinimalApi/Dockerfile.
COPY BillsMinimalApi.Contracts/BillsMinimalApi.Contracts.csproj BillsMinimalApi.Contracts/
COPY bills-frontend/BillsFrontEndBlazor/BillsFrontEndBlazor.csproj bills-frontend/BillsFrontEndBlazor/
RUN dotnet restore bills-frontend/BillsFrontEndBlazor/BillsFrontEndBlazor.csproj

COPY BillsMinimalApi.Contracts/ BillsMinimalApi.Contracts/
COPY bills-frontend/BillsFrontEndBlazor/ bills-frontend/BillsFrontEndBlazor/
# Deliberately NOT --no-restore, unlike BillsMinimalApi/Dockerfile. On .NET 10
# the framework's browser assets (_framework/blazor.server.js) come from the
# Microsoft.AspNetCore.App.Internal.Assets package, which the SDK auto-references
# only when RequiresAspNetWebAssets is true — and that is computed from whether
# the project has .razor Content items (Microsoft.NET.Sdk.Web.ProjectSystem
# .targets). The restore layer above sees only the .csproj, so it resolves false
# and the package is left out of the graph. Restoring again here, with the
# sources present, is what puts blazor.server.js in wwwroot; skip it and the app
# serves prerendered HTML with a 404 on its own JS and no interactivity at all.
RUN dotnet publish bills-frontend/BillsFrontEndBlazor/BillsFrontEndBlazor.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BillsFrontEndBlazor.dll"]
