#!/bin/bash

dotnet publish "$PWD/src/LanguageServer/LanguageServer.csproj" -f netcoreapp2.0 -o "$PWD/out/language-server"
dotnet public "$PWD/src/LanguageServer.TaskReflection/LanguageServer.TaskReflection.csproj" -f netcoreapp2.0 -o "$PWD/out/task-reflection"
