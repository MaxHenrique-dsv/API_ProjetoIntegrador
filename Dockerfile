FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["API_Strava.csproj", "./"]
RUN dotnet restore "./API_Strava.csproj"

COPY . .
RUN dotnet build "API_Strava.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "API_Strava.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "API_Strava.dll"]