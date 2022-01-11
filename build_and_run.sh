#!/bin/bash
git pull
set -ex
dotnet restore
dotnet tool restore
dotnet build -c Release
dotnet ef migrations add "Build-$(date +%s)" --no-build
dotnet ef database update --no-build
dotnet run
