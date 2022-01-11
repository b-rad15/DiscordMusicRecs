#!/bin/bash
set -x
dotnet new tool-manifest # if you are setting up this repo
dotnet tool install --local dotnet-ef

