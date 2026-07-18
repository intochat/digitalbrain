$replacements = @(
    # Specific type/interface renames
    @("ICallSeamTarget", "ICallNeuronTarget"),
    @("IPredicateSeamTarget", "IPredicateNeuronTarget"),
    @("IResourceSeamTarget", "IResourceNeuronTarget"),
    @("IStreamSeamTarget", "IStreamNeuronTarget"),
    @("ISeamHost", "INeuronHost"),
    @("StubSeamHost", "StubNeuronHost"),
    @("ProductionSeamHost", "ProductionNeuronHost"),
    @("PredicateSeamBinding", "PredicateNeuronBinding"),
    @("SeamCatalogInvariantHostedService", "NeuronCatalogInvariantHostedService"),
    @("SeamCatalogInvariantVerifier", "NeuronCatalogInvariantVerifier"),
    @("SeamBinding", "NeuronBinding"),
    @("GivenSeamReturns", "GivenNeuronReturns"),
    @("SeamReturns", "NeuronReturns"),
    @("LlmSeamGrain", "LlmNeuronGrain"),
    @("SlmSeamGrain", "SlmNeuronGrain"),
    @("AspireRuntimeSeamGrain", "AspireRuntimeNeuronGrain"),
    @("WindowsRuntimeSeamGrain", "WindowsRuntimeNeuronGrain"),
    @("TestAppStartedSeamGrain", "TestAppStartedNeuronGrain"),
    @("TestLocalLlmSeamGrain", "TestLocalLlmNeuronGrain"),
    @("TestDbSeamGrain", "TestDbNeuronGrain"),
    @("IntegrationGptSeamGrain", "IntegrationGptNeuronGrain"),
    @("TestLifecycleSeamGrain", "TestLifecycleNeuronGrain"),
    @("TestNavWatchSeamGrain", "TestNavWatchNeuronGrain"),
    @("TestGptSeamGrain", "TestGptNeuronGrain"),

    # FQNs and general string patterns
    @("BrainOS.Ai.LlmSeam", "BrainOS.Ai.LlmNeuron"),
    @("BrainOS.Ai.SlmSeam", "BrainOS.Ai.SlmNeuron"),
    @("Test.AppStartedSeam", "Test.AppStartedNeuron"),
    @("Test.LocalLlmSeam", "Test.LocalLlmNeuron"),
    @("Test.GptSeam", "Test.GptNeuron"),
    @("Test.DbSeam", "Test.DbNeuron"),

    # Test names and variables
    @("SeamProjectionTests", "NeuronProjectionTests"),
    @("SeamSpecDir", "NeuronSpecDir"),
    @("SeamCatalog", "NeuronCatalog"),
    @("SeamScenarios", "NeuronScenarios"),
    @("Seam_scenario_passes", "Neuron_scenario_passes"),
    @("Seam_spec_is_discoverable", "Neuron_spec_is_discoverable"),
    @("SeamTargetFqn", "NeuronTargetFqn"),
    @("SeamTarget", "NeuronTarget"),
    @("seamTarget", "neuronTarget"),

    # Generic renames
    @("seams", "neurons"),
    @("Seams", "Neurons"),
    @("seam", "neuron"),
    @("Seam", "Neuron")
)

# Find all files matching specified extensions
$extensions = "*.cs", "*.ino", "*.approved.txt", "*.csproj", "*.feature"
$files = Get-ChildItem -Path "E:\digitalbrain" -Recurse | Where-Object {
    $ext = $_.Extension
    ($ext -eq ".cs" -or $ext -eq ".ino" -or $ext -eq ".txt" -or $ext -eq ".csproj" -or $ext -eq ".feature") -and
    $_.FullName -notlike "*\bin\*" -and
    $_.FullName -notlike "*\obj\*" -and
    $_.FullName -notlike "*\.git\*" -and
    $_.FullName -notlike "*\.agents\*"
}

Write-Host "Found $($files.Count) files to process."

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $modified = $false
    
    foreach ($pair in $replacements) {
        $search = $pair[0]
        $replace = $pair[1]
        
        if ($content.Contains($search)) {
            $content = $content.Replace($search, $replace)
            $modified = $true
        }
    }
    
    if ($modified) {
        [System.IO.File]::WriteAllText($file.FullName, $content)
        Write-Host "Updated: $($file.FullName)" -ForegroundColor Green
    }
}
