FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Edge/Gateway.Yarp/Gateway.Yarp.csproj", "Edge/Gateway.Yarp/"]
RUN dotnet restore "Edge/Gateway.Yarp/Gateway.Yarp.csproj"
COPY . .
WORKDIR "/src/Edge/Gateway.Yarp"
RUN dotnet build "./Gateway.Yarp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Gateway.Yarp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Yarp.dll"]
