# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "ArkRealDealScrapper/ArkRealDealScrapper.Worker.csproj"
RUN dotnet publish "ArkRealDealScrapper/ArkRealDealScrapper.Worker.csproj" -c Release -o /app/publish

# ===== Runtime stage =====
# Version must match the Microsoft.Playwright NuGet package version (currently 1.58.0)
FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-noble AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ArkRealDealScrapper.Worker.dll"]
