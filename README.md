[![CI](https://github.com/marcin-krystianc/NuExporter/actions/workflows/ci.yml/badge.svg?branch=master&event=push)](https://github.com/marcin-krystianc/NuExporter/actions/workflows/ci.yml?query=branch%3Amaster+event%3Apush)
[![](https://img.shields.io/nuget/vpre/NuExporter)](https://www.nuget.org/packages/NuExporter/absoluteLatest)

NuExporter is a dotnet that helps investigate NuGet restore issues.
It lets you prepare a solution, which is stripped of any sensitive information, so it can be shared publicly.

# How to install it
`dotnet tool install --global nuexporter`

# How to use it

## Exporting solution to a json file
```nuexporter^
 --solution-file=<solution_file_path>^
 --output-path=<output_path>^
 --anonymize=false^
 --public-packages-source=https://api.nuget.org/v3/index.json
 ```
## Importing solution from a json file
`nuexporter --output-path=<output_path>`

# Which files are created by `NuExporter`

## solution.json
A file containing information about exported projects, their properties and references.
 
## packages.json
A file containing information about private/internal NuGet packages. NuExporter queries `public-packages-source` to check whether a NuGet package is public or private.
This file is created only if there are any private packages.

## nuget.config
Created only when there are any private packages.

## mapping.txt
Mapping of original names to anonymized ones.

## solution/
Directory with imported solution from solution.json file.

## packges/
Directory with imported solution from packages.json file.