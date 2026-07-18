const { spawn } = require('child_process');

function callTool(command, args, toolName, toolArguments) {
    return new Promise((resolve, reject) => {
        const proc = spawn(command, args, { shell: true });

        let stdoutData = '';
        let stderrData = '';

        proc.stdout.on('data', (data) => {
            stdoutData += data.toString();
            try {
                const lines = stdoutData.trim().split('\n');
                for (const line of lines) {
                    if (line.trim().startsWith('{')) {
                        const parsed = JSON.parse(line.trim());
                        if (parsed.id === 100) {
                            proc.kill();
                            resolve(parsed);
                            return;
                        }
                    }
                }
            } catch (e) {}
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
            reject(new Error(`Exit ${code}. Stderr: ${stderrData}`));
        });

        const req = {
            jsonrpc: "2.0",
            method: "tools/call",
            params: {
                name: toolName,
                arguments: toolArguments
            },
            id: 100
        };
        proc.stdin.write(JSON.stringify(req) + '\n');
    });
}

(async () => {
    try {
        console.log("=== CODEGRAPH SEARCH ===");
        const searchResult = await callTool('npx', ['-y', '@colbymchenry/codegraph@latest', 'serve', '--mcp'], 'codegraph_search', {
            query: "GrokUiDesignerNeuron",
            projectPath: "e:\\digitalbrain"
        });
        console.log("Search Result:\n", JSON.stringify(searchResult, null, 2));

        console.log("\n=== CODEGRAPH CONTEXT FOR TASK ===");
        const contextResult = await callTool('npx', ['-y', '@colbymchenry/codegraph@latest', 'serve', '--mcp'], 'codegraph_context', {
            task: "Summarize how GrokUiDesignerNeuron interacts with the gRPC gateway.",
            maxNodes: 5,
            includeCode: true,
            projectPath: "e:\\digitalbrain"
        });
        console.log("Context Result:\n", JSON.stringify(contextResult, null, 2));

    } catch (e) {
        console.error(e);
    }
})();
