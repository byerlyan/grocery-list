FROM node:22-alpine AS client-build
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS server-build
WORKDIR /src
COPY server/Groceries.Api.csproj server/
RUN dotnet restore server/Groceries.Api.csproj
COPY server/ server/
COPY --from=client-build /src/server/wwwroot/ server/wwwroot/
RUN dotnet publish server/Groceries.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/app/data/groceries.db" \
    DataProtection__Path=/app/data/keys
COPY --from=server-build /app/publish/ ./
RUN mkdir -p /app/data && chown -R "$APP_UID:$APP_UID" /app/data
USER $APP_UID
VOLUME ["/app/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://127.0.0.1:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "Groceries.Api.dll"]
