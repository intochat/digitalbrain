using Grpc.Net.Client;

namespace Ino.NeuronTesting.Internals;

// Aspire dev silos serve HTTP/2 over a self-signed cert; tests must opt out of validation.
public static class InoGrpcChannelFactory
{
    public static GrpcChannel ForKernel(string kernelHttpsUrl) =>
        GrpcChannel.ForAddress(kernelHttpsUrl, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            },
        });
}
