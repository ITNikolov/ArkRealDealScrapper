# Single-stage: playwright/dotnet already includes .NET SDK + Chromium
# Version must match the Microsoft.Playwright NuGet package version (currently 1.58.0)
FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-noble

WORKDIR /src
COPY . .

RUN dotnet restore "ArkRealDealScrapper/ArkRealDealScrapper.Worker.csproj"
RUN dotnet publish "ArkRealDealScrapper/ArkRealDealScrapper.Worker.csproj" \
    -c Release -o /app --no-restore

WORKDIR /app
ENTRYPOINT ["dotnet", "ArkRealDealScrapper.Worker.dll"]
