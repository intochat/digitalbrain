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
        console.log("=== CONTEXT7 RESOLVE-LIBRARY-ID ===");
        const resolveResult = await callTool('npx', ['-y', '@upstash/context7-mcp@latest'], 'resolve-library-id', {
            libraryName: "Next.js",
            query: "how to fetch data in app router"
        });
        console.log("Resolve Result:\n", JSON.stringify(resolveResult, null, 2));

        // If we get a valid library ID, let's query it
        const libId = resolveResult.result?.content?.[0]?.text;
        // Let's parse out the library ID or try to use a standard library ID if we couldn't parse
        let libraryId = "/vercel/next.js"; // standard fallback
        if (libId) {
            // Context7 resolve tool returns a structured markdown, let's search it for /vercel/next.js or similar ID
            const match = libId.match(/\/[\w-]+\/[\w\.-]+/);
            if (match) {
                libraryId = match[0];
            }
        }

        console.log(`\nUsing Library ID: ${libraryId}`);

        console.log("\n=== CONTEXT7 QUERY-DOCS ===");
        const queryResult = await callTool('npx', ['-y', '@upstash/context7-mcp@latest'], 'query-docs', {
            libraryId: libraryId,
            query: "React Server Component async data fetching example"
        });
        console.log("Query Docs Result:\n", JSON.stringify(queryResult, null, 2));

    } catch (e) {
        console.error(e);
    }
})();
