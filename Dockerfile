# playwright/dotnet includes .NET SDK + Chromium
FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-noble

# Xvfb provides a virtual display so Chrome runs with Headless=false
# without Cloudflare detecting a headless browser
RUN apt-get update && apt-get install -y xvfb && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .

RUN dotnet restore "ArkRealDealScrapper/ArkRealDealScrapper.Worker.csproj"
RUN dotnet publish "ArkRealDealScrapper/ArkRealDealScrapper.Worker.csproj" \
    -c Release -o /app --no-restore

WORKDIR /app
COPY ArkRealDealScrapper/entrypoint.sh .
RUN chmod +x entrypoint.sh

ENTRYPOINT ["./entrypoint.sh"]
