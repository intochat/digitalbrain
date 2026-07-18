using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class AstShapeTests
{
    [Fact]
    public void Doc_can_be_constructed_and_holds_parts()
    {
        var sp = SourceSpan.Empty;
        var port = new UsingDecl(PortSigil.Synapse, "ask", PortKind.Synapse, "A.Req", null, sp);
        var handler = new Handler(
            new PortTrigger("ask", sp), null,
            [new LogStmt(new StringExpr("hi", sp), sp)], sp);
        var doc = new NeuronDoc("Acme.X", "intent", [port], ["c1"],
            [handler], [], null, sp);

        doc.Fqn.Should().Be("Acme.X");
        doc.Usings.Should().ContainSingle(u => u.Name == "ask");
        doc.Counters.Should().Equal("c1");
        doc.Handlers.Single().Body.Single().Should().BeOfType<LogStmt>();
    }
}
