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

# Restore against the csproj alone so the (slow) restore layer is only
# invalidated by dependency changes, not by every source edit.
COPY BillsMinimalApi.csproj ./
RUN dotnet restore BillsMinimalApi.csproj

COPY . .
RUN dotnet publish BillsMinimalApi.csproj \
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
