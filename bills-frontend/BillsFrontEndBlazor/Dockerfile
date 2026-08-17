# syntax=docker/dockerfile:1

# See BillsMinimalApi/Dockerfile for why these tags (arm64-native, and ICU is
# present so ToString("C") renders "$" rather than the invariant "¤").
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY BillsFrontEndBlazor.csproj ./
RUN dotnet restore BillsFrontEndBlazor.csproj

COPY . .
RUN dotnet publish BillsFrontEndBlazor.csproj \
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
ENTRYPOINT ["dotnet", "BillsFrontEndBlazor.dll"]
