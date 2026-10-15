FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/Game.Core/ src/Game.Core/
COPY src/Game.DirectoryApi/ src/Game.DirectoryApi/
COPY data/ data/
RUN dotnet publish src/Game.DirectoryApi -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0.11
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Game.DirectoryApi.dll", "--urls", "http://+:8080"]
