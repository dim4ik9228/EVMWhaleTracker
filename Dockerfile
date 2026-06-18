FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG BUILD_CONFIGURATION=Release

COPY ["src/Presentation.Telegram/Presentation.Telegram.csproj", "src/Presentation.Telegram/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]

RUN dotnet restore "src/Presentation.Telegram/Presentation.Telegram.csproj"

COPY . .

RUN dotnet publish "src/Presentation.Telegram/Presentation.Telegram.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Presentation.Telegram.dll"]

FROM final AS debug
RUN apt-get update \
    && apt-get install -y --no-install-recommends bash curl unzip procps \
    && curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg \
    && rm -rf /var/lib/apt/lists/*
