#!/bin/bash
git pull
set -ex
dotnet build
dotnet ef migrations add "Build-$(date +%s)" --no-build
dotnet ef database update --no-build
dotnet run
