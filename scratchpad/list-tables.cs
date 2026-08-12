#:package Azure.Data.Tables@12.11.0
using Azure.Data.Tables;
var conn = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/MABAhMMTQ==;TableEndpoint=http://127.0.0.1:18987/devstoreaccount1;";
var c = new TableServiceClient(conn);
await foreach (var t in c.QueryAsync())
    Console.WriteLine(t.Name);
