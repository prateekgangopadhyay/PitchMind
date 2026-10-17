FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
ARG VITE_DEPLOYMENT_VERSION=local
ARG VITE_BUILD_ID=
ENV VITE_DEPLOYMENT_VERSION=$VITE_DEPLOYMENT_VERSION
ENV VITE_BUILD_ID=$VITE_BUILD_ID
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props PitchMind.slnx ./
COPY src/ src/
RUN dotnet publish src/PitchMind.Api/PitchMind.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=web /web/dist ./wwwroot/
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PitchMind.Api.dll"]
