#!/bin/bash
set -e

# Start a virtual display so Chrome can run with Headless=false
# This avoids Cloudflare headless browser detection
Xvfb :99 -screen 0 1280x720x24 -ac &
sleep 2

export DISPLAY=:99
echo "Virtual display :99 started"

exec dotnet ArkRealDealScrapper.Worker.dll
