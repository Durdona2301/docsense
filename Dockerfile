FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/DocSense.Web/DocSense.Web.csproj src/DocSense.Web/
COPY src/DocSense.Web/packages.lock.json src/DocSense.Web/
RUN dotnet restore src/DocSense.Web/DocSense.Web.csproj --locked-mode
COPY src/DocSense.Web/ src/DocSense.Web/
RUN dotnet publish src/DocSense.Web/DocSense.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocSense.Web.dll"]
