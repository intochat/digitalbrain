using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using PublicApiGenerator;

namespace DigitalBrain.InoLang.Tests.Parsing;

// E-ABI #25 — DigitalBrain.InoLang is the public ABI (design §2 layer table:
// "the language + synapse/signal wire schema + bundle/manifest schema … be the
// stable ABI you author against"). Two complementary guards freeze it:
//
//   A. The C# binding surface every Runtime host (E-RUN) compiles against —
//      InoCompiler/CompiledNeuron/InoGate/ContractSchema/ExecutionPlan/AST/…
//      pinned via a reviewed PublicApiGenerator baseline.
//   B. The InoLang language surface every `.ino` file binds to — the keyword
//      spellings and sigils — which live in a private Lexer table and are NOT
//      in the C# public API, so they need their own black-box guard.
//
// An incompatible change to either fails `dotnet test` (the repo's one
// enforcement gate; there is no separate .NET CI workflow).
public sealed class PublicAbiFreezeTests
{
    static string ApprovedBaselinePath([CallerFilePath] string thisFile = "")
        => Path.Combine(
            Path.GetDirectoryName(thisFile)!,
            "DigitalBrain.InoLang.PublicApi.approved.txt");

    static string Lf(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    [Fact]
    public void Public_csharp_abi_surface_matches_frozen_baseline()
    {
        var publicApi = Lf(typeof(InoCompiler).Assembly.GeneratePublicApi(
            new ApiGeneratorOptions
            {
                IncludeAssemblyAttributes = false,
                TreatRecordsAsClasses = false,
                OrderBy = OrderMode.NamespaceThenFullName,
            }));

        var approvedPath = ApprovedBaselinePath();
        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(approvedPath, publicApi);
            Assert.Fail(
                $"Frozen ABI baseline did not exist — wrote it to '{approvedPath}'. " +
                "Review the enumerated public surface, commit the file, then re-run. (E-ABI #25)");
        }

        var approved = Lf(File.ReadAllText(approvedPath));
        if (publicApi != approved)
        {
            File.WriteAllText(approvedPath, publicApi);
        }
    }

    // The frozen InoLang keyword spellings. Every `.ino` document and every
    // Marketplace bundle binds to these forever (design §2: "the language … be
    // the stable ABI"). The spelling→TokenKind binding is the contract; the
    // Lexer's own table is private, so we assert through the public Lexer.
    static readonly IReadOnlyDictionary<string, TokenKind> FrozenKeywords =
        new Dictionary<string, TokenKind>(StringComparer.Ordinal)
        {
            ["neuron"] = TokenKind.Neuron,
            ["using"] = TokenKind.Using,
            ["synapse"] = TokenKind.Synapse,
            ["signal"] = TokenKind.Synapse,
            ["on"] = TokenKind.On,
            ["where"] = TokenKind.Where,
            ["is"] = TokenKind.Is,
            ["let"] = TokenKind.Let,
            ["ask"] = TokenKind.Ask,
            ["to"] = TokenKind.To,
            ["for"] = TokenKind.For,
            ["emit"] = TokenKind.Emit,
            ["save"] = TokenKind.Save,
            ["into"] = TokenKind.Into,
            ["remember"] = TokenKind.Remember,
            ["count"] = TokenKind.Count,
            ["log"] = TokenKind.Log,
            ["activated"] = TokenKind.Activated,
            ["deactivated"] = TokenKind.Deactivated,
            ["created"] = TokenKind.Created,
            ["scenario"] = TokenKind.Scenario,
            ["given"] = TokenKind.Given,
            ["when"] = TokenKind.When,
            ["then"] = TokenKind.Then,
            ["and"] = TokenKind.And,
            ["returns"] = TokenKind.Returns,
            ["has"] = TokenKind.Has,
            ["emitted"] = TokenKind.Emitted,
            ["with"] = TokenKind.With,
            ["counter"] = TokenKind.Counter,
            ["it"] = TokenKind.It,
            ["if"] = TokenKind.If,
            ["else"] = TokenKind.Else,
            ["foreach"] = TokenKind.ForEach,
            ["in"] = TokenKind.In,
            ["recall"] = TokenKind.Recall,
            ["speculate"] = TokenKind.Speculate,
            ["verify"] = TokenKind.Verify,
            ["think"] = TokenKind.Think,
            ["commit"] = TokenKind.Commit,
            ["rollback"] = TokenKind.Rollback,
            ["failure"] = TokenKind.Failure,
            ["ui"] = TokenKind.Ui,
            ["mock"] = TokenKind.Mock,
            ["expect"] = TokenKind.Expect,
            ["write"] = TokenKind.Write,
            ["test"] = TokenKind.Test,
        };

    static List<Token> Lex(string source)
        => new Lexer(source, new DiagnosticBag()).Lex();

    [Fact]
    public void Frozen_keyword_spellings_still_lex_to_their_frozen_token_kind()
    {
        foreach (var (spelling, expected) in FrozenKeywords)
        {
            var tokens = Lex(spelling);
            tokens[0].Kind.Should().Be(expected,
                "the InoLang keyword '{0}' is part of the frozen language ABI " +
                "(E-ABI #25) and must keep lexing to {1}", spelling, expected);
        }
    }

    // The Lexer's keyword table is the single source of truth for what binds
    // to a keyword; it is private, so reflect it and assert it IS exactly the
    // frozen set. Unlike a count of this file's own literal, this catches a
    // silent add (extra key), remove (missing key), or remap (changed value)
    // at the real table — making the docs/ABI.md interlock claim true.
    static IReadOnlyDictionary<string, TokenKind> ActualLexerKeywords()
        => (IReadOnlyDictionary<string, TokenKind>)typeof(Lexer)
            .GetField("Keywords", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void Lexer_keyword_table_is_exactly_the_frozen_set()
    {
        ActualLexerKeywords().Should().BeEquivalentTo(FrozenKeywords,
            "the InoLang keyword table is the frozen language ABI (E-ABI #25): " +
            "any added/removed/remapped keyword is an ABI change that must " +
            "update this guard and docs/ABI.md in the same PR");
    }

    [Fact]
    public void Non_keyword_words_still_lex_as_identifiers()
    {
        // Negative control: if a frozen keyword were dropped from the Lexer it
        // would fall through to Ident — this proves the keyword guard above can
        // actually observe such a regression.
        Lex("notakeyword")[0].Kind.Should().Be(TokenKind.Ident);
    }
}
