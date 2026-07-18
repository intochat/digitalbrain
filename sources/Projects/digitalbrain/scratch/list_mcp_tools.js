const { spawn } = require('child_process');

function runMcpServer(command, args, inputJson) {
    return new Promise((resolve, reject) => {
        console.log(`Starting: ${command} ${args.join(' ')}`);
        const proc = spawn(command, args, { shell: true });

        let stdoutData = '';
        let stderrData = '';

        proc.stdout.on('data', (data) => {
            stdoutData += data.toString();
            // MCP responses are delimited by newlines
            try {
                // If we get a complete JSON-RPC response, we can try to parse it
                const lines = stdoutData.trim().split('\n');
                for (const line of lines) {
                    if (line.trim().startsWith('{')) {
                        const parsed = JSON.parse(line.trim());
                        if (parsed.id === inputJson.id) {
                            proc.kill();
                            resolve(parsed);
                            return;
                        }
                    }
                }
            } catch (e) {
                // Keep reading until we get a valid full response
            }
        });

        proc.stderr.on('data', (data) => {
            stderrData += data.toString();
        });

        proc.on('close', (code) => {
            if (stdoutData) {
                try {
                    const lines = stdoutData.trim().split('\n');
                    for (const line of lines) {
                        if (line.trim().startsWith('{')) {
                            resolve(JSON.parse(line.trim()));
                            return;
                        }
                    }
                } catch (e) {}
            }
            reject(new Error(`Process exited with code ${code}. Stderr: ${stderrData}\nStdout: ${stdoutData}`));
        });

        // Write the JSON-RPC request to stdin
        const requestStr = JSON.stringify(inputJson) + '\n';
        proc.stdin.write(requestStr);
    });
}

async function verify() {
    console.log("=== VERIFYING CODEGRAPH MCP ===");
    try {
        const codegraphResult = await runMcpServer('npx', ['-y', '@colbymchenry/codegraph@latest', 'serve', '--mcp'], {
            jsonrpc: "2.0",
            method: "tools/list",
            params: {},
            id: 1
        });
        console.log("CodeGraph Tools:\n", JSON.stringify(codegraphResult, null, 2));
    } catch (err) {
        console.error("CodeGraph Error:", err.message);
    }

    console.log("\n=== VERIFYING CONTEXT7 MCP ===");
    try {
        const context7Result = await runMcpServer('npx', ['-y', '@upstash/context7-mcp@latest'], {
            jsonrpc: "2.0",
            method: "tools/list",
            params: {},
            id: 2
        });
        console.log("Context7 Tools:\n", JSON.stringify(context7Result, null, 2));
    } catch (err) {
        console.error("Context7 Error:", err.message);
    }
}

verify();
