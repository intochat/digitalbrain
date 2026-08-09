using System;
using System.Collections.Generic;
using System.Linq;
using DigitalBrain.Poc.Creator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DigitalBrain.Poc.Creator.Tests;

public sealed class ForbiddenSyntaxFacts
{
    public static IEnumerable<object[]> ForbiddenForms =>
    [
        [ "System.IO.File.ReadAllText", CandidatePolicyError.ForbiddenSymbol ],
        [ "System.Net.Http.HttpClient", CandidatePolicyError.ForbiddenSymbol ],
        [ "System.Diagnostics.Process.Start", CandidatePolicyError.ForbiddenSymbol ],
        [ "System.Environment.GetEnvironmentVariable", CandidatePolicyError.ForbiddenSymbol ],
        [ "System.Console.WriteLine", CandidatePolicyError.ForbiddenSymbol ],
        [ "System.Reflection.Assembly", CandidatePolicyError.ForbiddenSymbol ],
        [ "typeof", CandidatePolicyError.ForbiddenConstruct ],
        [ "GetType", CandidatePolicyError.ForbiddenSymbol ],
        [ "object", CandidatePolicyError.ForbiddenSymbol ],
        [ "ServiceProvider", CandidatePolicyError.ForbiddenSymbol ],
        [ "GrainFactory", CandidatePolicyError.ForbiddenSymbol ],
        [ "IGrainBase", CandidatePolicyError.ForbiddenSymbol ],
        [ "Task.Run", CandidatePolicyError.ForbiddenSymbol ],
        [ "System.Threading.Timer", CandidatePolicyError.ForbiddenSymbol ],
        [ "Parallel.For", CandidatePolicyError.ForbiddenSymbol ],
        [ "for", CandidatePolicyError.ForbiddenConstruct ],
        [ "foreach", CandidatePolicyError.ForbiddenConstruct ],
        [ "while", CandidatePolicyError.ForbiddenConstruct ],
        [ "dynamic", CandidatePolicyError.ForbiddenConstruct ],
        [ "unsafe", CandidatePolicyError.ForbiddenConstruct ],
        [ "DllImport", CandidatePolicyError.ForbiddenSymbol ],
        [ "top-level statement", CandidatePolicyError.ForbiddenConstruct ],
        [ "recursive helper", CandidatePolicyError.RecursiveCall ],
        [ "static initializer", CandidatePolicyError.ForbiddenConstruct ],
        [ "ModuleInitializer", CandidatePolicyError.ForbiddenConstruct ],
        [ "#:package", CandidatePolicyError.FixedHeaderMismatch ],
        [ "#:include", CandidatePolicyError.FixedHeaderMismatch ],
        [ "changed #:sdk", CandidatePolicyError.FixedHeaderMismatch ],
        [ "changed #:project", CandidatePolicyError.FixedHeaderMismatch ],
        [ "changed #:property", CandidatePolicyError.FixedHeaderMismatch ],
        [ "unapproved constructor service", CandidatePolicyError.ForbiddenConstructor ],
        [ "IHandle<ChartPointAdded>", CandidatePolicyError.UnauthorizedTrigger ],
        [ "new SocialPostObserved", CandidatePolicyError.UnauthorizedOutput ],
        [ "trusted alias collision", CandidatePolicyError.AliasCollision ],
        [ "foreign AddChartPoint", CandidatePolicyError.UnauthorizedTarget ],
        [ "malformed IHandle", CandidatePolicyError.UnauthorizedTrigger ],
        [ "malformed Alias", CandidatePolicyError.AliasCollision ],
    ];

    [Theory]
    [MemberData(nameof(ForbiddenForms))]
    public void PolicyMutationIsRejectedWithTypedError(
        string forbiddenForm,
        CandidatePolicyError expected)
    {
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var valid = new ElonChartSyntaxFactory().Create(intent);
        var mutated = Mutate(valid.Source, forbiddenForm);

        var result = new CandidateSourceValidator().Validate(intent, mutated);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Error);
    }

    private static string Mutate(string source, string form)
    {
        if (form.StartsWith("#:", StringComparison.Ordinal))
        {
            return source.Replace(
                "#:sdk Microsoft.NET.Sdk\n",
                $"#:sdk Microsoft.NET.Sdk\n{form} example\n",
                StringComparison.Ordinal);
        }

        if (form == "changed #:sdk")
        {
            return source.Replace("#:sdk Microsoft.NET.Sdk", "#:sdk Other.Sdk", StringComparison.Ordinal);
        }

        if (form == "changed #:project")
        {
            return source.Replace(
                "DigitalBrain.Poc.Social.Contracts.csproj",
                "DigitalBrain.Poc.Untrusted.csproj",
                StringComparison.Ordinal);
        }

        if (form == "changed #:property")
        {
            return source.Replace("PublishAot=false", "PublishAot=true", StringComparison.Ordinal);
        }

        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        return form switch
        {
            "top-level statement" => root.WithMembers(root.Members.Insert(
                    0,
                    SyntaxFactory.GlobalStatement(
                        SyntaxFactory.ExpressionStatement(NumericLiteral(1)))))
                .ToFullString(),
            "trusted alias collision" => ReplaceAlias(root, "db.poc.social.post-observed.v1"),
            "foreign AddChartPoint" => ReplaceLiteral(root, "elon-chart", "foreign-chart"),
            "IHandle<ChartPointAdded>" => ReplaceTrigger(root),
            "malformed IHandle" => AddTriggerArgument(root),
            "malformed Alias" => AddAliasArgument(root),
            "unapproved constructor service" => AddConstructor(root),
            "static initializer" => AddMember(root, "private static int MutantValue = 1;"),
            "ModuleInitializer" => AddMember(root,
                "[System.Runtime.CompilerServices.ModuleInitializer] public static void MutantInitialize() { }"),
            "DllImport" => AddMember(root,
                "[System.Runtime.InteropServices.DllImport(\"mutant\")] private static extern void MutantImport();"),
            "recursive helper" => AddMember(root, "private void MutantHelper() { MutantHelper(); }"),
            _ => AddStatement(root, StatementFor(form)),
        };
    }

    private static string StatementFor(string form) => form switch
    {
        "System.IO.File.ReadAllText" => "System.IO.File.ReadAllText(\"mutant\");",
        "System.Net.Http.HttpClient" => "_ = new System.Net.Http.HttpClient();",
        "System.Diagnostics.Process.Start" => "System.Diagnostics.Process.Start(\"mutant\");",
        "System.Environment.GetEnvironmentVariable" => "System.Environment.GetEnvironmentVariable(\"mutant\");",
        "System.Console.WriteLine" => "System.Console.WriteLine(\"mutant\");",
        "System.Reflection.Assembly" => "System.Reflection.Assembly.GetExecutingAssembly();",
        "typeof" => "_ = typeof(string);",
        "GetType" => "_ = GetType();",
        "object" => "object mutant = new object();",
        "ServiceProvider" => "System.IServiceProvider? mutant = null;",
        "GrainFactory" => "Orleans.IGrainFactory? mutant = null;",
        "IGrainBase" => "Orleans.IGrainBase? mutant = null;",
        "Task.Run" => "System.Threading.Tasks.Task.Run(() => { });",
        "System.Threading.Timer" => "_ = new System.Threading.Timer(_ => { });",
        "Parallel.For" => "System.Threading.Tasks.Parallel.For(0, 1, _ => { });",
        "for" => "for (var i = 0; i < 1; i++) { }",
        "foreach" => "foreach (var item in System.Array.Empty<int>()) { }",
        "while" => "while (false) { }",
        "dynamic" => "dynamic mutant = null;",
        "unsafe" => "unsafe { int* mutant = null; }",
        "new SocialPostObserved" =>
            "_ = new SocialPostObserved(\"post\", \"author\", default);",
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
    };

    private static string AddStatement(CompilationUnitSyntax root, string statement)
    {
        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                "Mutant")
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword))
            .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement(statement)));
        return AddMember(root, method).ToFullString();
    }

    private static string AddMember(CompilationUnitSyntax root, string member) =>
        AddMember(root, SyntaxFactory.ParseMemberDeclaration(member) ??
            throw new InvalidOperationException("The mutation member did not parse."))
        .ToFullString();

    private static CompilationUnitSyntax AddMember(CompilationUnitSyntax root, MemberDeclarationSyntax member)
    {
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Last();
        return root.ReplaceNode(type, type.AddMembers(member));
    }

    private static string ReplaceAlias(CompilationUnitSyntax root, string alias)
    {
        var argument = root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .First(attribute => attribute.Name.ToString() == "Alias")
            .ArgumentList!.Arguments.Single();
        return root.ReplaceNode(
                argument.Expression,
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(alias)))
            .ToFullString();
    }

    private static string ReplaceLiteral(CompilationUnitSyntax root, string current, string replacement)
    {
        var literal = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Single(candidate => candidate.Token.ValueText == current);
        return root.ReplaceNode(
                literal,
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(replacement)))
            .ToFullString();
    }

    private static string ReplaceTrigger(CompilationUnitSyntax root)
    {
        var trigger = root.DescendantNodes()
            .OfType<GenericNameSyntax>()
            .First(name => name.Identifier.ValueText == "IHandle" &&
                name.TypeArgumentList.Arguments.Single().ToString() == "SocialPostObserved");
        return root.ReplaceNode(
                trigger.TypeArgumentList.Arguments.Single(),
                SyntaxFactory.IdentifierName("ChartPointAdded"))
            .ToFullString();
    }

    private static string AddTriggerArgument(CompilationUnitSyntax root)
    {
        var trigger = root.DescendantNodes()
            .OfType<GenericNameSyntax>()
            .First(name => name.Identifier.ValueText == "IHandle");
        return root.ReplaceNode(
                trigger.TypeArgumentList,
                trigger.TypeArgumentList.AddArguments(SyntaxFactory.IdentifierName("ElonPostMatched")))
            .ToFullString();
    }

    private static string AddAliasArgument(CompilationUnitSyntax root)
    {
        var alias = root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .First(attribute => attribute.Name.ToString() == "Alias");
        return root.ReplaceNode(
                alias.ArgumentList!,
                alias.ArgumentList!.AddArguments(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("second")))))
            .ToFullString();
    }

    private static string AddConstructor(CompilationUnitSyntax root)
    {
        var neuron = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(type => type.Identifier.ValueText == "ElonPostRuleNeuron");
        var constructor = SyntaxFactory.ConstructorDeclaration(neuron.Identifier)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("serviceProvider"))
                .WithType(SyntaxFactory.ParseTypeName("System.IServiceProvider")))
            .WithBody(SyntaxFactory.Block());
        return root.ReplaceNode(neuron, neuron.AddMembers(constructor)).ToFullString();
    }

    private static LiteralExpressionSyntax NumericLiteral(int value) =>
        SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(value));
}
