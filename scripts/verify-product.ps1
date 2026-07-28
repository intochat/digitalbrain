[CmdletBinding()]
param(
    [string]$Prompt = "Who are you and what can you actually do?",
    [string]$ChatName = "product-verification-$([Guid]::NewGuid().ToString('N'))",
    [ValidateRange(1, 180)]
    [int]$TimeoutSeconds = 180,
    [switch]$KeepRunning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repository = Split-Path -Parent $PSScriptRoot
$appHost = Join-Path $repository "os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj"
$started = $false

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Command,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Command $($Arguments -join ' ')' exited with code $LASTEXITCODE."
    }
}

function Invoke-AspireJson {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = @(& aspire @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "'aspire $($Arguments -join ' ')' exited with code $LASTEXITCODE."
    }

    $lines = @($output | ForEach-Object { $_.ToString() })
    if ($lines.Count -eq 0) {
        return $null
    }

    $jsonStart = 0..($lines.Count - 1) |
        Where-Object { $lines[$_].TrimStart() -match "^[\[\{]" } |
        Select-Object -First 1
    if ($null -eq $jsonStart) {
        return $null
    }

    return ($lines[$jsonStart..($lines.Count - 1)] -join "`n") |
        ConvertFrom-Json
}

function Require {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

Push-Location $repository
try {
    & aspire stop --apphost $appHost --non-interactive 2>$null

    Invoke-Checked dotnet @("build", "DigitalBrain.slnx", "-c", "Release")
    Invoke-Checked aspire @(
        "start",
        "--apphost", $appHost,
        "--format", "Json",
        "--non-interactive")
    $started = $true

    foreach ($resource in @(
        "silo",
        "digitalbrain-mcp",
        "digitalbrain-ui",
        "brain-ai-gemma4",
        "digitalbrain-flutter")) {
        Invoke-Checked aspire @(
            "wait", $resource,
            "--apphost", $appHost,
            "--timeout", "180",
            "--non-interactive")
    }

    $uiDescription = Invoke-AspireJson @(
        "describe", "digitalbrain-ui",
        "--apphost", $appHost,
        "--format", "Json",
        "--non-interactive")
    $uiResource = @($uiDescription.resources) |
        Where-Object displayName -eq "digitalbrain-ui" |
        Select-Object -First 1
    $uiUrl = @($uiResource.urls) |
        Where-Object name -eq "http" |
        Select-Object -ExpandProperty url -First 1
    Require `
        -Condition (-not [string]::IsNullOrWhiteSpace($uiUrl)) `
        -Message "Aspire did not expose the digitalbrain-ui HTTP endpoint."

    $tools = @()
    for ($attempt = 1; $attempt -le 90 -and $tools.Count -eq 0; $attempt++) {
        $tools = @(
            Invoke-AspireJson @(
                "mcp", "tools",
                "--apphost", $appHost,
                "--format", "Json",
                "--non-interactive"))
        if ($tools.Count -eq 0) {
            Start-Sleep -Seconds 1
        }
    }
    $toolNames = @($tools | ForEach-Object tool)
    Write-Host "Discovered MCP tools: $($toolNames -join ', ')"
    $expectedTools = @(
        "list_active_neurons",
        "read_chat_transcript",
        "read_neuron_journal",
        "send_chat_message")
    Require `
        -Condition ($toolNames.Count -eq $expectedTools.Count) `
        -Message "Expected $($expectedTools.Count) MCP tools but found $($toolNames.Count)."
    foreach ($tool in $expectedTools) {
        Require `
            -Condition ($toolNames -contains $tool) `
            -Message "MCP tool '$tool' was not discovered."
    }
    Require `
        -Condition ($toolNames -notcontains "ask_llama32") `
        -Message "The retired model-specific MCP tool is still exposed."

    $sendInput = @{
        text = $Prompt
        chatName = $ChatName
        timeoutSeconds = $TimeoutSeconds
    } | ConvertTo-Json -Compress
    $result = Invoke-AspireJson @(
        "mcp", "call",
        "digitalbrain-mcp", "send_chat_message",
        "--input", $sendInput,
        "--apphost", $appHost,
        "--non-interactive")

    Require `
        -Condition (-not [string]::IsNullOrWhiteSpace($result.response)) `
        -Message "DigitalBrain returned an empty assistant response."
    Require `
        -Condition ($result.commandId -match "^[0-9a-f]{32}$") `
        -Message "The MCP result did not return a valid command ID."
    Require `
        -Condition ($result.correlationId -match "^[0-9a-f]{32}$") `
        -Message "The MCP result did not return a valid correlation ID."

    $chatInput = @{ chatName = $ChatName } | ConvertTo-Json -Compress
    $transcript = Invoke-AspireJson @(
        "mcp", "call",
        "digitalbrain-mcp", "read_chat_transcript",
        "--input", $chatInput,
        "--apphost", $appHost,
        "--non-interactive")
    Require `
        -Condition ($transcript.turns.Count -eq 2) `
        -Message "Expected one user and one assistant transcript turn."
    Require `
        -Condition (
            $transcript.turns[0].speaker -eq "you" -and
            $transcript.turns[0].text -eq $Prompt) `
        -Message "The durable transcript does not contain the sent user turn."
    Require `
        -Condition (
            $transcript.turns[1].speaker -eq "brain" -and
            $transcript.turns[1].text -eq $result.response) `
        -Message "The durable transcript does not contain the returned assistant turn."

    $journalInput = @{
        grainType = "chat"
        name = $ChatName
        kind = "outgoing"
        afterSequence = 0
    } | ConvertTo-Json -Compress
    $journal = Invoke-AspireJson @(
        "mcp", "call",
        "digitalbrain-mcp", "read_neuron_journal",
        "--input", $journalInput,
        "--apphost", $appHost,
        "--non-interactive")
    Require `
        -Condition ($journal.entries.Count -eq 2) `
        -Message "Expected exactly two outgoing chat journal entries."
    Require `
        -Condition (
            $journal.entries[0].synapse -eq "UserMessaged" -and
            $journal.entries[1].synapse -eq "AssistantResponded") `
        -Message "Chat journal entries are missing or out of order."
    Require `
        -Condition (
            $journal.entries[0].correlation -eq $result.correlationId -and
            $journal.entries[1].correlation -eq $result.correlationId) `
        -Message "Chat journal entries do not share the MCP result correlation."

    $expectedModules = @(
        "DigitalBrain.AI.AIModule",
        "DigitalBrain.Chat.ChatModule",
        "DigitalBrain.Flutter.FlutterModule",
        "DigitalBrain.Google.GoogleModule",
        "DigitalBrain.OS.OSBehaviorsModule",
        "DigitalBrain.Salesforce.SalesforceModule")
    $expectedChatNeuron = "chat:dev/$ChatName"
    $topology = $null
    $moduleIds = @()
    $activeChat = $null
    for ($attempt = 1; $attempt -le 30 -and $null -eq $activeChat; $attempt++) {
        try {
            $topology = Invoke-RestMethod -Uri "$uiUrl/brain/topology"
            $moduleIds = @($topology.modules | ForEach-Object id)
            $activeChat = @($topology.neurons) |
                Where-Object id -eq $expectedChatNeuron |
                Select-Object -First 1
        }
        catch {
            $topology = $null
        }
        if ($null -eq $activeChat) {
            Start-Sleep -Seconds 1
        }
    }
    Require `
        -Condition ($null -ne $topology) `
        -Message "The UI edge did not return a live brain topology."
    Require `
        -Condition ($moduleIds.Count -eq $expectedModules.Count) `
        -Message "Expected $($expectedModules.Count) live modules but found $($moduleIds.Count)."
    foreach ($module in $expectedModules) {
        Require `
            -Condition ($moduleIds -contains $module) `
            -Message "Live topology is missing configured module '$module'."
    }
    Require `
        -Condition ($null -ne $activeChat) `
        -Message "Live topology never exposed active neuron '$expectedChatNeuron'."

    $genAi = $null
    for ($attempt = 1; $attempt -le 60 -and $null -eq $genAi; $attempt++) {
        $genAiSpans = @(
            Invoke-AspireJson @(
                "otel", "spans",
                "--apphost", $appHost,
                "--format", "Json",
                "--limit", "50",
                "--search", "gen_ai",
                "--non-interactive"))
        $genAi = $genAiSpans |
            Where-Object {
                $_.name -like "chat gemma4:*" -and
                $_.attributes.'gen_ai.operation.name' -eq "chat" -and
                $_.attributes.'gen_ai.provider.name' -eq "ollama"
            } |
            Select-Object -First 1
        if ($null -eq $genAi) {
            Start-Sleep -Seconds 1
        }
    }
    Require `
        -Condition ($null -ne $genAi) `
        -Message "No Gemma4 OpenTelemetry chat span was exported."
    Require `
        -Condition (
            -not $genAi.attributes.PSObject.Properties[
                "gen_ai.input.messages"] -and
            -not $genAi.attributes.PSObject.Properties[
                "gen_ai.output.messages"]) `
        -Message "The GenAI span exported sensitive message content."

    $completion = $null
    for ($attempt = 1; $attempt -le 60 -and $null -eq $completion; $attempt++) {
        $toolLogs = @(
            Invoke-AspireJson @(
                "otel", "logs",
                "--apphost", $appHost,
                "--format", "Json",
                "--limit", "20",
                "--search", "send_chat_message",
                "--non-interactive"))
        $completion = $toolLogs |
            Where-Object {
                $_.attributes.ToolName -eq "send_chat_message" -and
                $_.attributes.IsError -eq "false"
            } |
            Select-Object -First 1
        if ($null -eq $completion) {
            Start-Sleep -Seconds 1
        }
    }
    Require `
        -Condition ($null -ne $completion) `
        -Message "No successful structured MCP completion log was exported."

    [pscustomobject]@{
        Chat = $result.chat
        CommandId = $result.commandId
        CorrelationId = $result.correlationId
        Response = $result.response
        GenAiTraceId = $genAi.traceId
        GenAiDurationMs = $genAi.durationMs
        InputTokens = $genAi.attributes.'gen_ai.usage.input_tokens'
        OutputTokens = $genAi.attributes.'gen_ai.usage.output_tokens'
        TopologyModules = $moduleIds.Count
        ActiveNeuron = $activeChat.id
    } | Format-List
}
finally {
    Pop-Location

    if ($started -and -not $KeepRunning) {
        & aspire stop --apphost $appHost --non-interactive
    }
}
