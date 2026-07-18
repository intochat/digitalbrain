const http = require('http');

function postJson(url, headers, body) {
    return new Promise((resolve, reject) => {
        const u = new URL(url);
        const reqBody = JSON.stringify(body);
        const options = {
            hostname: u.hostname,
            port: u.port,
            path: u.pathname,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Content-Length': Buffer.byteLength(reqBody),
                ...headers
            }
        };

        const req = http.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => { data += chunk; });
            res.on('end', () => {
                resolve({
                    statusCode: res.statusCode,
                    headers: res.headers,
                    body: data
                });
            });
        });

        req.on('error', (e) => reject(e));
        req.write(reqBody);
        req.end();
    });
}

function parseMcpResponse(body) {
    // Standard MCP SSE response is:
    // event: message
    // data: {"result":...}
    console.log("Raw Response chunk:\n", body);
    const lines = body.split('\n');
    for (const line of lines) {
        if (line.startsWith('data: ')) {
            const jsonPart = line.substring(6).trim();
            return JSON.parse(jsonPart);
        }
    }
    // Fallback
    return JSON.parse(body);
}

(async () => {
    try {
        console.log("Querying MCP Tools List...");
        const listResult = await postJson(
            'http://localhost:5810/mcp',
            { 'Accept': 'application/json, text/event-stream' },
            { jsonrpc: "2.0", method: "tools/list", params: {}, id: 1 }
        );

        console.log("Status Code:", listResult.statusCode);
        const parsed = parseMcpResponse(listResult.body);
        console.log("\nTools found:", JSON.stringify(parsed.result?.tools?.map(t => t.name) || parsed, null, 2));

        console.log("\nQuerying list_neurons tool...");
        const callResult = await postJson(
            'http://localhost:5810/mcp',
            { 'Accept': 'application/json, text/event-stream' },
            {
                jsonrpc: "2.0",
                method: "tools/call",
                params: {
                    name: "list_neurons",
                    arguments: {}
                },
                id: 2
            }
        );

        console.log("Call Status Code:", callResult.statusCode);
        const parsedCall = parseMcpResponse(callResult.body);
        console.log("\nlist_neurons tool result:", JSON.stringify(parsedCall.result || parsedCall, null, 2));

        console.log("\nQuerying brain tool to access Google Cloud / Gmail...");
        const brainResult = await postJson(
            'http://localhost:5810/mcp',
            { 'Accept': 'application/json, text/event-stream' },
            {
                jsonrpc: "2.0",
                method: "tools/call",
                params: {
                    name: "brain",
                    arguments: {
                        prompt: "make a summary on my last 10 emails and put it into a report.md on d drive",
                        timeoutSeconds: 30
                    }
                },
                id: 3
            }
        );

        console.log("Brain Tool Status Code:", brainResult.statusCode);
        const parsedBrain = parseMcpResponse(brainResult.body);
        console.log("\nbrain tool result:", JSON.stringify(parsedBrain.result || parsedBrain, null, 2));

        // Validation assertion
        const resultText = parsedBrain.result?.content?.[0]?.text || parsedBrain.result || "";
        if (resultText.includes("consent_required") && resultText.includes("GoogleAuthCard")) {
            console.log("\nSUCCESS: Successfully returned Google OAuth consent card via RFW!");
        } else {
            console.error("\nFAILURE: Response does not contain consent_required or GoogleAuthCard.");
            process.exit(1);
        }

    } catch (e) {
        console.error("Error occurred:", e);
        process.exit(1);
    }
})();
