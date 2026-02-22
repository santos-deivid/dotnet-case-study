FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Application/SentinelService/SentinelService.csproj", "Application/SentinelService/"]
RUN dotnet restore "Application/SentinelService/SentinelService.csproj"
COPY . .
WORKDIR "/src/Application/SentinelService"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SentinelService.dll"]
