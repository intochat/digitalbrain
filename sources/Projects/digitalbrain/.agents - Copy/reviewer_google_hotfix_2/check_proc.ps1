Get-Process | Where-Object { $_.Name -like '*dotnet*' -or $_.Name -like '*Brain*' -or $_.Name -like '*Digital*' -or $_.Name -like '*sqlite*' } | Select-Object Name, Id, Path
