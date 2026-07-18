const { spawn } = require('child_process');

function runMcpServer(command, args, inputJson) {
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
                        if (parsed.id === inputJson.id) {
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

        proc.stdin.write(JSON.stringify(inputJson) + '\n');
    });
}

(async () => {
    try {
        const res = await runMcpServer('npx', ['-y', '@colbymchenry/codegraph@latest', 'serve', '--mcp'], {
            jsonrpc: "2.0",
            method: "tools/list",
            params: {},
            id: 1
        });
        const tools = res.result.tools;
        console.log("=== CODEGRAPH TOOLS ===");
        for (const t of tools) {
            console.log(`- ${t.name}: ${t.description}`);
            console.log("  Input Schema properties:", Object.keys(t.inputSchema.properties).join(", "));
        }
    } catch (e) {
        console.error(e);
    }
})();
