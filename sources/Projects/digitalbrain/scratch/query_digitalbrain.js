const http = require('http');

function postJson(url, body) {
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
                'Accept': 'application/json'
            }
        };

        const req = http.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => { data += chunk; });
            res.on('end', () => {
                resolve({
                    statusCode: res.statusCode,
                    body: data
                });
            });
        });

        req.on('error', (e) => reject(e));
        req.write(reqBody);
        req.end();
    });
}

(async () => {
    try {
        console.log("Listing tools from digitalbrain-mcp at http://localhost:5810/mcp...");
        const response = await postJson('http://localhost:5810/mcp', {
            jsonrpc: "2.0",
            method: "tools/list",
            params: {},
            id: 1
        });
        
        console.log("Status:", response.statusCode);
        const parsed = JSON.parse(response.body);
        console.log("Tools response:", JSON.stringify(parsed, null, 2));
    } catch (err) {
        console.error("Error:", err);
    }
})();
