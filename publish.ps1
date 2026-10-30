# Publishes every tool into one folder, so adding that folder to PATH adds them all. The folder is ToolsPublishDir in
# Directory.Build.props. A project is published if it has a FolderProfile, so a new tool needs nothing added here.
$ErrorActionPreference = "Stop"

$publishProfiles = Get-ChildItem "$PSScriptRoot/*/src/*/Properties/PublishProfiles/FolderProfile.pubxml"

foreach ($publishProfile in $publishProfiles) {
    $projectFolder = $publishProfile.Directory.Parent.Parent
    $project = Get-ChildItem $projectFolder.FullName -Filter *.csproj

    Write-Host "Publishing $($project.BaseName)"
    dotnet publish $project.FullName -c Release -p:PublishProfile=FolderProfile
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
