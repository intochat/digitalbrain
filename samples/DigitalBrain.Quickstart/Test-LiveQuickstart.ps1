param(
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$quickstartRoot = $PSScriptRoot
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $quickstartRoot '../..'))
$appHost = [System.IO.Path]::GetFullPath(
    (Join-Path $quickstartRoot 'DigitalBrain.Quickstart.AppHost/DigitalBrain.Quickstart.AppHost.csproj'))
$aspireConfig = Join-Path $repositoryRoot 'aspire.config.json'
$evidenceRoot = Join-Path $repositoryRoot 'artifacts/quickstart-live'
$pathComparison = [System.StringComparison]::OrdinalIgnoreCase

function Assert-NativeSuccess {
    param(
        [string]$Operation
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Convert-AspireJson {
    param(
        [object[]]$Output,
        [string]$Operation
    )

    $text = $Output -join [Environment]::NewLine
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "$Operation returned no JSON."
    }
    try {
        return $text | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "$Operation returned invalid JSON: $($_.Exception.Message)"
    }
}

function Get-NormalizedPath {
    param(
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    try {
        return [System.IO.Path]::GetFullPath($Path).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }
    catch {
        return $null
    }
}

function Get-OptionalPropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Get-RedactedUri {
    param(
        [object]$Value
    )

    $text = [string]$Value
    $uri = $null
    if ([string]::IsNullOrWhiteSpace($text) -or
        -not [Uri]::TryCreate($text, [UriKind]::Absolute, [ref]$uri)) {
        return [string]::Empty
    }

    $builder = [UriBuilder]::new($uri)
    $builder.Query = [string]::Empty
    $builder.Fragment = [string]::Empty
    $builder.UserName = [string]::Empty
    $builder.Password = [string]::Empty
    return $builder.Uri.AbsoluteUri
}

function Test-TraceSpan {
    param(
        [object]$Trace,
        [string]$SpanName,
        [string]$AttributeName,
        [string]$AttributeValue
    )

    foreach ($span in @(Get-OptionalPropertyValue $Trace 'spans')) {
        if ([string](Get-OptionalPropertyValue $span 'name') -ne $SpanName) {
            continue
        }
        if ([string]::IsNullOrWhiteSpace($AttributeName)) {
            return $true
        }

        $attributes = Get-OptionalPropertyValue $span 'attributes'
        if ([string](Get-OptionalPropertyValue $attributes $AttributeName) -eq
            $AttributeValue) {
            return $true
        }
    }
    return $false
}

function Get-AspireProcesses {
    $output = @(aspire ps --format Json --non-interactive --nologo)
    Assert-NativeSuccess 'aspire ps'
    $result = Convert-AspireJson $output 'aspire ps'
    return @($result)
}

function Test-QuickstartRunning {
    param(
        [object[]]$Processes
    )

    foreach ($process in $Processes) {
        if ($null -eq $process) {
            continue
        }
        $candidate = Get-NormalizedPath ([string]$process.appHostPath)
        if ($null -ne $candidate -and
            $candidate.Equals($appHost, $pathComparison)) {
            return $true
        }
    }
    return $false
}

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Set-ProcessEnvironment {
    param(
        [string]$Name,
        [AllowNull()]
        [string]$Value
    )

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Get-Resource {
    param(
        [object]$Description,
        [string]$DisplayName
    )

    $matches = @($Description.resources | Where-Object {
            [string]$_.displayName -eq $DisplayName
        })
    if ($matches.Count -ne 1) {
        throw "Expected one '$DisplayName' resource, found $($matches.Count)."
    }
    return $matches[0]
}

function Get-HttpResourceUri {
    param(
        [object]$Resource
    )

    $matches = @($Resource.urls | Where-Object {
            [string]$_.name -eq 'http'
        })
    if ($matches.Count -ne 1) {
        throw "Resource '$($Resource.displayName)' does not expose one HTTP URL."
    }
    $uri = [Uri]$matches[0].url
    if (-not $uri.IsAbsoluteUri -or
        -not $uri.IsLoopback -or
        $uri.Scheme -notin @([Uri]::UriSchemeHttp, [Uri]::UriSchemeHttps)) {
        throw "Resource '$($Resource.displayName)' exposed a non-loopback HTTP URL."
    }
    return $uri
}

function Get-ExecutableProcessId {
    param(
        [object]$Resource
    )

    if ($null -eq $Resource.properties) {
        return $null
    }
    $property = $Resource.properties.PSObject.Properties['executable.pid']
    $processIdValue = 0
    if ($null -ne $property -and
        [int]::TryParse([string]$property.Value, [ref]$processIdValue) -and
        $processIdValue -gt 0) {
        return $processIdValue
    }
    return $null
}

function Get-ContainerId {
    param(
        [object]$Resource
    )

    if ($null -eq $Resource.properties) {
        return $null
    }
    $property = $Resource.properties.PSObject.Properties['container.id']
    if ($null -eq $property -or
        [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return $null
    }
    return [string]$property.Value
}

function Get-InputHash {
    param(
        [string]$Text
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($bytes)).
        ToLowerInvariant()
}

function Invoke-JsonPost {
    param(
        [Uri]$Uri,
        [hashtable]$Body
    )

    return Invoke-RestMethod `
        -Uri $Uri `
        -Method Post `
        -ContentType 'application/json' `
        -Body ($Body | ConvertTo-Json -Depth 20 -Compress) `
        -TimeoutSec 60
}

function Invoke-JsonGetWithRetry {
    param(
        [Uri]$Uri,
        [int]$Attempts = 30
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 30
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Get-ProviderRequests {
    param(
        [Uri]$Uri
    )

    $response = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 30
    if ($response -is [System.Array]) {
        foreach ($request in $response) {
            Write-Output $request
        }
        return
    }
    if ($null -ne $response) {
        Write-Output $response
    }
}

function Assert-Turn {
    param(
        [object]$Turn,
        [string]$TurnId,
        [string]$Response,
        [long]$Revision,
        [int]$TurnCount
    )

    $normalizedTurnId = ([Guid]$TurnId).ToString('N')
    if ([string]$Turn.turnId -ne $normalizedTurnId -or
        [string]$Turn.role -ne 'balanced' -or
        [string]$Turn.response -ne $Response -or
        [long]$Turn.revision -ne $Revision -or
        [int]$Turn.turnCount -ne $TurnCount) {
        throw "The balanced turn result did not match the durable contract."
    }
}

function Assert-ProviderRequest {
    param(
        [object]$Request,
        [int]$Sequence,
        [string]$Provider,
        [string]$Model,
        [string]$ExpectedInput
    )

    if ([int]$Request.sequence -ne $Sequence -or
        [string]$Request.provider -ne $Provider -or
        [string]$Request.capability -ne 'chat' -or
        [string]$Request.model -ne $Model -or
        [string]$Request.inputHash -ne (Get-InputHash $ExpectedInput) -or
        -not [bool]$Request.authorized) {
        $actual = [ordered]@{
            sequence = [int]$Request.sequence
            provider = [string]$Request.provider
            capability = [string]$Request.capability
            model = [string]$Request.model
            inputHash = [string]$Request.inputHash
            authorized = [bool]$Request.authorized
        } | ConvertTo-Json -Compress
        $expected = [ordered]@{
            sequence = $Sequence
            provider = $Provider
            capability = 'chat'
            model = $Model
            inputHash = Get-InputHash $ExpectedInput
            authorized = $true
        } | ConvertTo-Json -Compress
        throw "Controlled provider request mismatch. Expected $expected; actual $actual."
    }
}

function Assert-HttpSuccess {
    param(
        [Uri]$Uri
    )

    $response = Invoke-WebRequest `
        -Uri $Uri `
        -Method Get `
        -SkipCertificateCheck `
        -TimeoutSec 30
    if ([int]$response.StatusCode -lt 200 -or
        [int]$response.StatusCode -ge 300) {
        throw "GET $Uri returned HTTP $($response.StatusCode)."
    }
}

function Test-LoopbackPortOpen {
    param(
        [int]$Port
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync(
            [System.Net.IPAddress]::Loopback,
            $Port)
        if (-not $connect.Wait(250)) {
            return $false
        }
        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Assert-PortClosed {
    param(
        [int]$Port
    )

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if (-not (Test-LoopbackPortOpen $Port)) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Loopback port $Port is still open after Aspire stopped."
}

function Assert-ProcessStopped {
    param(
        [int]$ProcessId
    )

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            $process = [System.Diagnostics.Process]::GetProcessById($ProcessId)
            if ($process.HasExited) {
                return
            }
        }
        catch [System.ArgumentException] {
            return
        }
        if ($attempt -lt 20) {
            Start-Sleep -Milliseconds 250
        }
    }
    throw "Process $ProcessId is still running after Aspire stopped."
}

$preflightProcesses = Get-AspireProcesses
if (Test-QuickstartRunning $preflightProcesses) {
    throw 'The DigitalBrain quickstart AppHost is already running.'
}

$providerPort = Get-FreeLoopbackPort
$driverPort = Get-FreeLoopbackPort
while ($driverPort -eq $providerPort) {
    $driverPort = Get-FreeLoopbackPort
}

$environmentNames = @(
    'DOTNET_ENVIRONMENT',
    'ASPNETCORE_ENVIRONMENT',
    'DigitalBrain__Quickstart__Live',
    'DigitalBrain__Quickstart__ProviderEndpoint',
    'DigitalBrain__Quickstart__ProviderPort',
    'DigitalBrain__Quickstart__DriverPort',
    'Parameters__digitalbrain-owner',
    'Parameters__brain-openai-openai-apikey',
    'Parameters__brain-anthropic-api-key',
    'OPENAI_API_KEY',
    'ANTHROPIC_API_KEY',
    'DigitalBrain__AI__OpenAI__ApiKey',
    'DigitalBrain__AI__Anthropic__ApiKey',
    'NUGET_PACKAGES'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] =
        [Environment]::GetEnvironmentVariable($name, 'Process')
}

$openAICredentialPresent =
    -not [string]::IsNullOrWhiteSpace($previousEnvironment['OPENAI_API_KEY'])
$anthropicCredentialPresent =
    -not [string]::IsNullOrWhiteSpace($previousEnvironment['ANTHROPIC_API_KEY'])
$openAISecret = "task9-openai-$([Guid]::NewGuid().ToString('N'))"
$anthropicSecret = "task9-anthropic-$([Guid]::NewGuid().ToString('N'))"
$owner = "task9-owner-$([Guid]::NewGuid().ToString('N'))"
$providerEndpoint = "http://127.0.0.1:$providerPort"
$packageCache = [System.IO.Path]::GetFullPath(
    (Join-Path $evidenceRoot "nuget-$([Guid]::NewGuid().ToString('N'))"))
$evidenceBoundary =
    [System.IO.Path]::GetFullPath($evidenceRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $packageCache.StartsWith(
        $evidenceBoundary,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The isolated NuGet cache escaped the live evidence directory.'
}
$configExisted = Test-Path -LiteralPath $aspireConfig
$configBytes = if ($configExisted) {
    [System.IO.File]::ReadAllBytes($aspireConfig)
}
else {
    $null
}
$operationError = $null
$cleanupErrors = [System.Collections.Generic.List[string]]::new()
$startAttempted = $false
$startResult = $null
$finalDescription = $null
$capturedProcessIds = [System.Collections.Generic.HashSet[int]]::new()
$capturedContainerIds = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)

try {
    Set-ProcessEnvironment 'DOTNET_ENVIRONMENT' 'Development'
    Set-ProcessEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-ProcessEnvironment 'DigitalBrain__Quickstart__Live' 'true'
    Set-ProcessEnvironment `
        'DigitalBrain__Quickstart__ProviderEndpoint' `
        $providerEndpoint
    Set-ProcessEnvironment `
        'DigitalBrain__Quickstart__ProviderPort' `
        ([string]$providerPort)
    Set-ProcessEnvironment `
        'DigitalBrain__Quickstart__DriverPort' `
        ([string]$driverPort)
    Set-ProcessEnvironment 'Parameters__digitalbrain-owner' $owner
    Set-ProcessEnvironment `
        'Parameters__brain-openai-openai-apikey' `
        $openAISecret
    Set-ProcessEnvironment `
        'Parameters__brain-anthropic-api-key' `
        $anthropicSecret
    Set-ProcessEnvironment 'OPENAI_API_KEY' $null
    Set-ProcessEnvironment 'ANTHROPIC_API_KEY' $null
    Set-ProcessEnvironment 'DigitalBrain__AI__OpenAI__ApiKey' $null
    Set-ProcessEnvironment 'DigitalBrain__AI__Anthropic__ApiKey' $null
    Set-ProcessEnvironment 'NUGET_PACKAGES' $packageCache
    New-Item -ItemType Directory -Path $packageCache -Force | Out-Null
    dotnet restore $appHost `
        --packages $packageCache `
        --force-evaluate `
        --nologo
    Assert-NativeSuccess 'dotnet restore'
    dotnet build $appHost `
        --no-restore `
        --no-incremental `
        --nologo
    Assert-NativeSuccess 'dotnet build'

    $startAttempted = $true
    $startOutput = @(
        aspire start `
            --apphost $appHost `
            --no-build `
            --format Json `
            --non-interactive `
            --nologo)
    Assert-NativeSuccess 'aspire start'
    $startResult = Convert-AspireJson $startOutput 'aspire start'
    foreach ($propertyName in @('appHostPid', 'cliPid')) {
        $processIdValue = 0
        if ([int]::TryParse(
                [string]$startResult.$propertyName,
                [ref]$processIdValue) -and
            $processIdValue -gt 0) {
            [void]$capturedProcessIds.Add($processIdValue)
        }
    }

    aspire wait test-provider --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait test-provider'
    aspire wait brain-discovery-storage --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait brain-discovery-storage'
    aspire wait brain-storage --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait brain-storage'
    aspire wait kernel --apphost $appHost --status up --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait kernel'
    aspire wait console-test-driver --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait console-test-driver'
    aspire wait orleans-dashboard --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait orleans-dashboard'
    aspire wait devui --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait devui'

    $descriptionOutput = @(
        aspire describe --apphost $appHost --format Json --include-hidden --non-interactive --nologo)
    Assert-NativeSuccess 'aspire describe'
    $description = Convert-AspireJson $descriptionOutput 'aspire describe'
    foreach ($resourceName in @(
            'test-provider',
            'brain-discovery-storage',
            'brain-storage',
            'kernel',
            'console-test-driver',
            'orleans-dashboard',
            'devui')) {
        $resource = Get-Resource $description $resourceName
        if ([string]$resource.state -ne 'Running') {
            throw "Resource '$resourceName' is not running."
        }
        if ($resourceName -ne 'kernel' -and
            [string]$resource.healthStatus -ne 'Healthy') {
            throw "Resource '$resourceName' is not healthy."
        }
    }

    $kernelResource = Get-Resource $description 'kernel'
    $kernelProcessId = Get-ExecutableProcessId $kernelResource
    if ($null -eq $kernelProcessId) {
        throw 'The kernel process id was not reported by Aspire.'
    }
    [void]$capturedProcessIds.Add($kernelProcessId)
    $orleansUrls = @($kernelResource.urls | Where-Object {
            [string]$_.name -in @('orleans-gateway', 'orleans-silo')
        })
    if ($orleansUrls.Count -ne 2) {
        throw 'The kernel did not expose both Orleans runtime endpoints.'
    }

    $providerResource = Get-Resource $description 'test-provider'
    $driverResource = Get-Resource $description 'console-test-driver'
    $dashboardResource = Get-Resource $description 'orleans-dashboard'
    $devUIResource = Get-Resource $description 'devui'
    $providerUri = Get-HttpResourceUri $providerResource
    $driverUri = Get-HttpResourceUri $driverResource
    $dashboardUri = Get-HttpResourceUri $dashboardResource
    $devUIUri = Get-HttpResourceUri $devUIResource
    if ($providerUri.Port -ne $providerPort -or
        $driverUri.Port -ne $driverPort) {
        throw 'Aspire did not preserve the controlled loopback ports.'
    }

    foreach ($property in $driverResource.environment.PSObject.Properties) {
        if ($property.Name -match 'OpenAI|Anthropic|DigitalBrain__AI__|Parameters__') {
            throw "The restricted client received privileged environment key '$($property.Name)'."
        }
    }

    if ($null -ne $startResult.dashboardUrl) {
        Assert-HttpSuccess ([Uri]$startResult.dashboardUrl)
    }

    $conversation = 'quickstart-recovery'
    $turn1Id = '11111111-1111-1111-1111-111111111111'
    $turn1Text = 'before-restart'
    $turn1Response =
        'controlled:anthropic:claude-sonnet-4-5:1:before-restart'
    $turnUri = [Uri]::new($driverUri, '/live/turn')
    $snapshotUri = [Uri]::new(
        $driverUri,
        "/live/conversations/$conversation")
    $requestsUri = [Uri]::new($providerUri, '/requests')
    $turn1Body = @{
        conversation = $conversation
        turnId = $turn1Id
        text = $turn1Text
    }
    $turn1 = Invoke-JsonPost $turnUri $turn1Body
    Assert-Turn $turn1 $turn1Id $turn1Response 1 1
    $providerRequests = @(Get-ProviderRequests $requestsUri)
    if ($providerRequests.Count -ne 1) {
        throw 'The first durable turn did not make exactly one provider call.'
    }
    Assert-ProviderRequest `
        $providerRequests[0] `
        1 `
        'anthropic' `
        'claude-sonnet-4-5' `
        $turn1Text

    aspire resource kernel restart --apphost $appHost --non-interactive --nologo
    Assert-NativeSuccess 'aspire resource kernel restart'
    aspire wait kernel --apphost $appHost --status up --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait kernel after restart'
    aspire wait console-test-driver --apphost $appHost --status healthy --timeout $TimeoutSeconds --non-interactive --nologo
    Assert-NativeSuccess 'aspire wait console-test-driver after restart'

    $restartDescriptionOutput = @(
        aspire describe --apphost $appHost --format Json --include-hidden --non-interactive --nologo)
    Assert-NativeSuccess 'aspire describe after restart'
    $restartDescription = Convert-AspireJson `
        $restartDescriptionOutput `
        'aspire describe after restart'
    $restartedKernel = Get-Resource $restartDescription 'kernel'
    $restartedKernelState = [string](
        Get-OptionalPropertyValue $restartedKernel 'state')
    if ($restartedKernelState -cne 'Running') {
        throw "The restarted kernel state was '$restartedKernelState', not Running."
    }
    $restartedKernelProcessId = Get-ExecutableProcessId $restartedKernel
    if ($null -eq $restartedKernelProcessId -or
        $restartedKernelProcessId -eq $kernelProcessId) {
        throw 'The kernel restart did not produce a new process.'
    }
    [void]$capturedProcessIds.Add($restartedKernelProcessId)
    Assert-ProcessStopped $kernelProcessId

    $snapshot = Invoke-JsonGetWithRetry $snapshotUri
    $snapshotTurns = @($snapshot.turns)
    if ([long]$snapshot.revision -ne 1 -or
        $snapshotTurns.Count -ne 1 -or
        [string]$snapshotTurns[0].turnId -ne ([Guid]$turn1Id).ToString('N') -or
        [string]$snapshotTurns[0].role -ne 'balanced' -or
        [string]$snapshotTurns[0].text -ne $turn1Text -or
        [string]$snapshotTurns[0].response -ne $turn1Response) {
        throw 'The committed conversation did not survive the kernel restart.'
    }

    $replayedTurn = Invoke-JsonPost $turnUri $turn1Body
    Assert-Turn $replayedTurn $turn1Id $turn1Response 1 1
    $providerRequests = @(Get-ProviderRequests $requestsUri)
    if ($providerRequests.Count -ne 1) {
        throw 'Replaying the committed turn called the provider again.'
    }

    $turn2Id = '22222222-2222-2222-2222-222222222222'
    $turn2Text = 'after-restart'
    $turn2Response =
        'controlled:anthropic:claude-sonnet-4-5:2:after-restart'
    $turn2 = Invoke-JsonPost $turnUri @{
        conversation = $conversation
        turnId = $turn2Id
        text = $turn2Text
    }
    Assert-Turn $turn2 $turn2Id $turn2Response 2 2
    $providerRequests = @(Get-ProviderRequests $requestsUri)
    if ($providerRequests.Count -ne 2) {
        $providerRequestSummary =
            $providerRequests | ConvertTo-Json -Depth 10 -Compress
        throw (
            'The recovered conversation did not make exactly two provider calls. ' +
            "Found $($providerRequests.Count): $providerRequestSummary")
    }
    Assert-ProviderRequest `
        $providerRequests[0] `
        1 `
        'anthropic' `
        'claude-sonnet-4-5' `
        $turn1Text
    Assert-ProviderRequest `
        $providerRequests[1] `
        2 `
        'anthropic' `
        'claude-sonnet-4-5' `
        $turn2Text

    Assert-HttpSuccess ([Uri]::new($dashboardUri, '/dashboard/version'))
    Assert-HttpSuccess ([Uri]::new($dashboardUri, '/dashboard/'))

    $entities = Invoke-RestMethod `
        -Uri ([Uri]::new($devUIUri, '/v1/entities/')) `
        -Method Get `
        -TimeoutSec 30
    $entityList = @($entities.entities)
    $entityNames = @($entityList | ForEach-Object {
            if (-not [string]::IsNullOrWhiteSpace([string]$_.id)) {
                [string]$_.id
            }
            else {
                [string]$_.name
            }
        } | Sort-Object)
    if (($entityNames -join ',') -ne 'balanced,fast,reasoning' -or
        @($entityList | Where-Object {
                [string]$_.type -ne 'agent'
            }).Count -ne 0) {
        throw 'DevUI did not discover exactly the three typed DigitalBrain agents.'
    }

    $devUIText = 'task9-devui'
    $devUIResponse = Invoke-JsonPost `
        ([Uri]::new($devUIUri, '/v1/responses/')) `
        @{
            agent = @{
                type = 'agent_reference'
                name = 'fast'
            }
            input = $devUIText
            stream = $false
        }
    $outputText = @(
        $devUIResponse.output |
            ForEach-Object { $_.content } |
            Where-Object { [string]$_.type -eq 'output_text' } |
            ForEach-Object { [string]$_.text })
    $expectedDevUIText =
        'controlled:openai:gpt-5-mini:3:task9-devui'
    if ([string]$devUIResponse.status -ne 'completed' -or
        $outputText.Count -ne 1 -or
        $outputText[0] -ne $expectedDevUIText) {
        throw 'DevUI did not complete the typed fast-agent turn.'
    }

    $providerRequests = @(Get-ProviderRequests $requestsUri)
    if ($providerRequests.Count -ne 3) {
        throw 'The controlled provider journal did not contain exactly three calls.'
    }
    Assert-ProviderRequest `
        $providerRequests[2] `
        3 `
        'openai' `
        'gpt-5-mini' `
        $devUIText

    $finalDescriptionOutput = @(
        aspire describe --apphost $appHost --format Json --include-hidden --non-interactive --nologo)
    Assert-NativeSuccess 'aspire describe final'
    $finalDescription = Convert-AspireJson `
        $finalDescriptionOutput `
        'aspire describe final'
    foreach ($resource in @($finalDescription.resources)) {
        $processIdValue = Get-ExecutableProcessId $resource
        if ($null -ne $processIdValue) {
            [void]$capturedProcessIds.Add($processIdValue)
        }
        $containerId = Get-ContainerId $resource
        if ($null -ne $containerId) {
            [void]$capturedContainerIds.Add($containerId)
        }
    }

    $kernelLogsOutput = @(
        aspire logs kernel --apphost $appHost --format Json --tail 200 --non-interactive --nologo)
    Assert-NativeSuccess 'aspire logs kernel'
    $kernelLogs = Convert-AspireJson $kernelLogsOutput 'aspire logs kernel'
    $driverLogsOutput = @(
        aspire logs console-test-driver --apphost $appHost --format Json --tail 200 --non-interactive --nologo)
    Assert-NativeSuccess 'aspire logs console-test-driver'
    $driverLogs = Convert-AspireJson `
        $driverLogsOutput `
        'aspire logs console-test-driver'
    $providerLogsOutput = @(
        aspire logs test-provider --apphost $appHost --format Json --tail 200 --non-interactive --nologo)
    Assert-NativeSuccess 'aspire logs test-provider'
    $providerLogs = Convert-AspireJson `
        $providerLogsOutput `
        'aspire logs test-provider'

    $kernelTraces = @()
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $traceOutput = @(
            aspire otel traces kernel --apphost $appHost --format Json -n 1000 --non-interactive --nologo 2>$null)
        if ($LASTEXITCODE -eq 0 -and $traceOutput.Count -gt 0) {
            try {
                $allKernelTraces = @(
                    Convert-AspireJson $traceOutput 'aspire otel traces kernel')
                $kernelTraces = @($allKernelTraces | Where-Object {
                        Test-TraceSpan `
                            $_ `
                            'chat claude-sonnet-4-5' `
                            'gen_ai.operation.name' `
                            'chat'
                    })
            }
            catch {
                $kernelTraces = @()
            }
        }
        if ($kernelTraces.Count -gt 0) {
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if ($kernelTraces.Count -eq 0 -or
        @($kernelTraces | Where-Object { [bool]$_.hasError }).Count -gt 0) {
        throw 'Kernel chat trace capture was empty or contained an error trace.'
    }

    $driverTraces = @()
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $traceOutput = @(
            aspire otel traces console-test-driver --apphost $appHost --format Json -n 100 --non-interactive --nologo 2>$null)
        if ($LASTEXITCODE -eq 0 -and $traceOutput.Count -gt 0) {
            try {
                $allDriverTraces = @(
                    Convert-AspireJson `
                        $traceOutput `
                        'aspire otel traces console-test-driver')
                $driverTraces = @($allDriverTraces | Where-Object {
                        Test-TraceSpan `
                            $_ `
                            'digitalbrain.conversation.submit' `
                            ([string]::Empty) `
                            ([string]::Empty)
                    })
            }
            catch {
                $driverTraces = @()
            }
        }
        if ($driverTraces.Count -gt 0) {
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if ($driverTraces.Count -eq 0 -or
        @($driverTraces | Where-Object { [bool]$_.hasError }).Count -gt 0) {
        throw 'Client conversation trace capture was empty or contained an error trace.'
    }

    $redactedResources = @($finalDescription.resources | ForEach-Object {
            $resource = $_
            $resourceUrls = @(
                Get-OptionalPropertyValue $resource 'urls')
            [ordered]@{
                displayName = [string](
                    Get-OptionalPropertyValue $resource 'displayName')
                resourceType = [string](
                    Get-OptionalPropertyValue $resource 'resourceType')
                state = [string](
                    Get-OptionalPropertyValue $resource 'state')
                healthStatus = [string](
                    Get-OptionalPropertyValue $resource 'healthStatus')
                urls = @($resourceUrls | ForEach-Object {
                        [ordered]@{
                            name = [string](
                                Get-OptionalPropertyValue $_ 'name')
                            url = Get-RedactedUri (
                                Get-OptionalPropertyValue $_ 'url')
                        }
                    })
            }
        })
    foreach ($resource in $redactedResources) {
        foreach ($resourceUrl in @($resource.urls)) {
            $urlText = [string]$resourceUrl.url
            if ([string]::IsNullOrWhiteSpace($urlText)) {
                continue
            }

            $redactedUri = [Uri]$urlText
            if (-not [string]::IsNullOrEmpty($redactedUri.Query) -or
                -not [string]::IsNullOrEmpty($redactedUri.Fragment) -or
                -not [string]::IsNullOrEmpty($redactedUri.UserInfo)) {
                throw 'The live evidence contained an unredacted resource URL.'
            }
        }
    }
    $evidence = [ordered]@{
        resources = $redactedResources
        providerRequests = $providerRequests
        kernelLogs = $kernelLogs
        driverLogs = $driverLogs
        providerLogs = $providerLogs
        kernelTraces = $kernelTraces
        driverTraces = $driverTraces
    }
    $evidenceJson = $evidence | ConvertTo-Json -Depth 100
    if ($evidenceJson.Contains($openAISecret, [StringComparison]::Ordinal) -or
        $evidenceJson.Contains($anthropicSecret, [StringComparison]::Ordinal)) {
        throw 'The live evidence contained a synthetic provider credential.'
    }
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    Set-Content `
        -LiteralPath (Join-Path $evidenceRoot 'task9-evidence.json') `
        -Value $evidenceJson `
        -Encoding utf8NoBOM

    Write-Host 'Controlled provider, durable restart, Dashboard, DevUI, and telemetry proofs passed.'
    if ($openAICredentialPresent) {
        Write-Host 'Optional real OpenAI credential detected; no external paid turn was required.'
    }
    else {
        Write-Host 'Optional real OpenAI turn skipped because no credential was present.'
    }
    if ($anthropicCredentialPresent) {
        Write-Host 'Optional real Anthropic credential detected; no external paid turn was required.'
    }
    else {
        Write-Host 'Optional real Anthropic turn skipped because no credential was present.'
    }
}
catch {
    $operationError = $_
}
finally {
    if ($startAttempted) {
        try {
            $stopOutput = @(
                aspire stop --apphost $appHost --non-interactive --nologo)
            $stopExitCode = $LASTEXITCODE
            if ($stopExitCode -ne 0) {
                $stillRunning = Test-QuickstartRunning (Get-AspireProcesses)
                if ($stillRunning) {
                    throw "aspire stop failed with exit code $stopExitCode."
                }
            }
        }
        catch {
            $cleanupErrors.Add($_.Exception.Message)
        }
    }

    foreach ($name in $environmentNames) {
        try {
            Set-ProcessEnvironment $name $previousEnvironment[$name]
        }
        catch {
            $cleanupErrors.Add(
                "Could not restore process environment '$name': $($_.Exception.Message)")
        }
    }

    try {
        if ($configExisted) {
            [System.IO.File]::WriteAllBytes($aspireConfig, $configBytes)
        }
        elseif (Test-Path -LiteralPath $aspireConfig) {
            Remove-Item -LiteralPath $aspireConfig -Force
        }
    }
    catch {
        $cleanupErrors.Add(
            "Could not restore aspire.config.json: $($_.Exception.Message)")
    }

    if ($startAttempted) {
        try {
            for ($attempt = 1; $attempt -le 20; $attempt++) {
                if (-not (Test-QuickstartRunning (Get-AspireProcesses))) {
                    break
                }
                if ($attempt -eq 20) {
                    throw 'The quickstart AppHost remains registered after stop.'
                }
                Start-Sleep -Milliseconds 250
            }
            Assert-PortClosed $providerPort
            Assert-PortClosed $driverPort
            foreach ($processIdValue in $capturedProcessIds) {
                Assert-ProcessStopped $processIdValue
            }
            if ($capturedContainerIds.Count -gt 0 -and
                $null -ne (Get-Command docker -ErrorAction SilentlyContinue)) {
                foreach ($containerId in $capturedContainerIds) {
                    $runningOutput = @(
                        docker inspect --format '{{.State.Running}}' $containerId 2>$null)
                    if ($LASTEXITCODE -eq 0 -and
                        ($runningOutput -join '').Trim() -eq 'true') {
                        throw "Container $containerId is still running after Aspire stopped."
                    }
                }
            }
        }
        catch {
            $cleanupErrors.Add($_.Exception.Message)
        }
    }

    try {
        if (Test-Path -LiteralPath $packageCache) {
            $cacheRemovalError = $null
            for ($attempt = 1; $attempt -le 40; $attempt++) {
                try {
                    [System.IO.Directory]::Delete($packageCache, $true)
                    $cacheRemovalError = $null
                    break
                }
                catch {
                    $cacheRemovalError = $_.Exception
                    if ($attempt -lt 40) {
                        Start-Sleep -Milliseconds 250
                    }
                }
            }
            if ($null -ne $cacheRemovalError) {
                throw $cacheRemovalError
            }
        }
    }
    catch {
        $cleanupErrors.Add(
            "Could not remove the isolated NuGet cache: $($_.Exception.Message)")
    }

    try {
        aspire ps --non-interactive
        Assert-NativeSuccess 'aspire ps final'
    }
    catch {
        $cleanupErrors.Add($_.Exception.Message)
    }
}

if ($null -ne $operationError) {
    if ($cleanupErrors.Count -gt 0) {
        throw (
            "$($operationError.Exception.Message) " +
            "Cleanup also failed: $($cleanupErrors -join ' ')")
    }
    throw $operationError
}
if ($cleanupErrors.Count -gt 0) {
    throw ($cleanupErrors -join ' ')
}
