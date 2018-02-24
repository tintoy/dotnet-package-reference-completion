#!/bin/bash

set -eo pipefail

VersionPrefix="$1"
VersionSuffix="$2"

if [[ ! $VersionPrefix ]]; then
    echo 'Must specify a version prefix.'

    exit 1
fi

ServerRoot="$PWD/lib/server"
PublishRoot="$PWD/out"

dotnet restore "$ServerRoot/MSBuildProjectTools.sln" /p:VersionPrefix="$VersionPrefix" /p:VersionSuffix="$VersionSuffix"
dotnet publish "$ServerRoot/src/LanguageServer/LanguageServer.csproj" -f netcoreapp2.0 -o "$PublishRoot/language-server" /p:VersionPrefix="$VersionPrefix" /p:VersionSuffix="$VersionSuffix"
dotnet publish "$ServerRoot/src/LanguageServer.TaskReflection/LanguageServer.TaskReflection.csproj" -f netcoreapp2.0 -o "$PublishRoot/task-reflection" /p:VersionPrefix="$VersionPrefix" /p:VersionSuffix="$VersionSuffix"
