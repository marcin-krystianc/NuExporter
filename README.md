[![CI](https://github.com/marcin-krystianc/NuExporter/actions/workflows/ci.yml/badge.svg?branch=master&event=push)](https://github.com/marcin-krystianc/NuExporter/actions/workflows/ci.yml?query=branch%3Amaster+event%3Apush)
[![](https://img.shields.io/nuget/vpre/NuExporter)](https://www.nuget.org/packages/NuExporter/absoluteLatest)

NuExporter is a dotnet that helps investigate NuGet restore issues.
It lets you prepare a solution, which is stripped of any sensitive information, so it can be shared publicly.

# How to use it
`dotnet tool install --global nuexporter`

`nuexporter --solution-file=<path> --output-path=d:\tmp --anonymize=false`

`nuexporter --output-path=d:\tmp`
