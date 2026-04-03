FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Edge/Gateway.Yarp/Gateway.Yarp.csproj", "Edge/Gateway.Yarp/"]
RUN dotnet restore "Edge/Gateway.Yarp/Gateway.Yarp.csproj"
COPY . .
WORKDIR "/src/Edge/Gateway.Yarp"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Yarp.dll"]