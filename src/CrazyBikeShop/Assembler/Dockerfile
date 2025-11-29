FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Assembler/Assembler.csproj", "Assembler/"]
COPY ["Shared/Shared.csproj", "Shared/"]
RUN dotnet restore "Assembler/Assembler.csproj"
COPY . .
WORKDIR "/src/Assembler"
RUN dotnet build "./Assembler.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Assembler.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CrazyBikeShop.Assembler.dll"]
