FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /source
COPY global.json ./
COPY src/TaskSpace.Api/TaskSpace.Api.csproj src/TaskSpace.Api/packages.lock.json src/TaskSpace.Api/
RUN dotnet restore src/TaskSpace.Api/TaskSpace.Api.csproj --locked-mode
COPY src/TaskSpace.Api/ src/TaskSpace.Api/
RUN dotnet publish src/TaskSpace.Api/TaskSpace.Api.csproj --configuration Release --no-restore --output /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
WORKDIR /app
# Urls переопределяет локальный адрес из appsettings.json.
ENV URLS=http://0.0.0.0:8080
ENV ConnectionStrings__TaskSpace="Data Source=/data/taskspace.db;Foreign Keys=True;Default Timeout=5"
RUN mkdir /data && chown "$APP_UID:$APP_UID" /data
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskSpace.Api.dll"]
