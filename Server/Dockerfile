FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY WordCraft.Server.csproj ./
RUN dotnet restore
COPY Program.cs Lobby.cs ./
RUN dotnet publish -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
USER $APP_UID
ENTRYPOINT ["dotnet", "WordCraft.Server.dll"]
