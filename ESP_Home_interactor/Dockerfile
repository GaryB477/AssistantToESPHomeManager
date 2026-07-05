FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so dependency layers cache across code changes
COPY ESP_Home_Interactor.csproj .
RUN dotnet restore ESP_Home_Interactor.csproj

COPY . .
RUN dotnet publish ESP_Home_Interactor.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5010
ENTRYPOINT ["dotnet", "ESP_Home_Interactor.dll"]
