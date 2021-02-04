$ProviderName = Read-Host -Prompt 'Enter the name for your Payment Provider'

Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "Initializing project - $ProviderName" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Write-Host "$(Get-Date)" -ForegroundColor DarkGray

# Rename directories
Write-Host ""
Write-Host "=============================================" -ForegroundColor Gray
Write-Host "Renaming directories" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Gray
Write-Host ""

Get-ChildItem -Recurse -Directory | ForEach-Object { 
    $newName = $_.Name.Replace("Template", $ProviderName)
    if (-NOT ($_.Name -eq $newName)) {
        Write-Host "Renaming directory $($_.fullname) to $newName"  -ForegroundColor DarkGray
        $newPath = Join-Path -Path $_.Parent.FullName -ChildPath $NewName
        Move-Item -Path $_.FullName -Destination $newPath -Force
    }
}

# Rename files
Write-Host ""
Write-Host "=============================================" -ForegroundColor Gray
Write-Host "Renaming files" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Gray
Write-Host ""

Get-ChildItem -Recurse -File  | ForEach-Object {
    $newName = $_.name.replace("Template", $ProviderName)
    if (-NOT ($_.name -eq $newName)) {
        Write-Host "Renaming file $($_.fullname) to $newName"  -ForegroundColor DarkGray
        Rename-Item -Path $_.FullName -NewName $newName -Force
    }
}

# Replace in files
Write-Host ""
Write-Host "=============================================" -ForegroundColor Gray
Write-Host "Replacing file contents" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Gray
Write-Host ""

Get-ChildItem -Recurse -File | Where-Object { ($_.Name -match "\.sln|\.proj|\.cs|\.md|\.nuspec|\.package\.xml") -and ($_.Name -notmatch "_tools") } | ForEach-Object { 
    Write-Host $_.FullName  -ForegroundColor DarkGray
    (Get-Content $_.FullName | ForEach-Object { 
        $_.Replace("Template", $ProviderName).Replace("template", $ProviderName.ToLower())
    }) | Set-Content $_.FullName
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "Project successfully initialized" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""