# Client and Sandbox

DigitalBrain provides secure programmability for untrusted or external workloads.

## DigitalBrain.Client

`DigitalBrain.Client` allows external processes to communicate with a running brain instance over HTTP+SSE:
- Endpoints: `/brains/{brainId}/runtime/v1/{invoke,report,stream}`.
- Authentication: HMAC-signed `SessionToken` issued by the host silo.
- Invocation: Strongly typed neuron proxies (`connection.Neuron<TNeuron>()`).

## Sandbox Isolation

Workloads running outside the host process execute in an isolated container environment:
- Container communication routes strictly through the HTTP edge.
- Every call passes through `CallFilter` policy evaluation.
- Secrets and vault state remain within the silo and never leak to the sandbox client.
