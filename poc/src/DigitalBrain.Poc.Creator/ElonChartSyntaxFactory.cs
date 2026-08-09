using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.Poc.Creator;

public sealed class ElonChartSyntaxFactory
{
    public CandidateShape Create(ElonChartAuthoringIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var root = BuildRoot(intent).NormalizeWhitespace(indentation: "    ", eol: "\n");
        var source = FixedCandidateHeader.Create(intent.Family) + root.ToFullString() + "\n";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        return new CandidateShape(["elon-chart.cs"], source, hash);
    }

    internal static CompilationUnitSyntax BuildRoot(ElonChartAuthoringIntent intent) =>
        SyntaxFactory.CompilationUnit()
            .AddUsings(
                Using("System", "Threading"),
                Using("System", "Threading", "Tasks"),
                Using("DigitalBrain", "Poc", "Abstractions"),
                Using("DigitalBrain", "Poc", "Charting", "Contracts"),
                Using("DigitalBrain", "Poc", "Social", "Contracts"),
                Using("Orleans"))
            .AddMembers(
                SyntaxFactory.FileScopedNamespaceDeclaration(
                        Name("DigitalBrain", "Poc", "Candidate", intent.Family.Value))
                    .AddMembers(
                        CreateMatchedSynapse(intent),
                        CreateRuleState(intent),
                        CreateRuleNeuron(intent),
                        CreateForwarderNeuron(intent)));

    private static RecordDeclarationSyntax CreateMatchedSynapse(ElonChartAuthoringIntent intent) =>
        SyntaxFactory.RecordDeclaration(
                SyntaxKind.RecordDeclaration,
                SyntaxFactory.Token(SyntaxKind.RecordKeyword),
                "ElonPostMatched")
            .AddAttributeLists(
                Attribute("GenerateSerializer"),
                Attribute("Alias", StringLiteral(LocalAlias(intent, "matched"))))
            .AddModifiers(Public(), Sealed())
            .WithParameterList(SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(
                [
                    SerializedParameter("PostId", SyntaxFactory.PredefinedType(
                        SyntaxFactory.Token(SyntaxKind.StringKeyword)), 0),
                    SerializedParameter("OccurredAt", Name("System", "DateTimeOffset"), 1),
                    SerializedParameter("RuleOrdinal", SyntaxFactory.PredefinedType(
                        SyntaxFactory.Token(SyntaxKind.IntKeyword)), 2),
                ])))
            .WithBaseList(SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                    SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("Synapse")))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    private static RecordDeclarationSyntax CreateRuleState(ElonChartAuthoringIntent intent) =>
        SyntaxFactory.RecordDeclaration(
                SyntaxKind.RecordDeclaration,
                SyntaxFactory.Token(SyntaxKind.RecordKeyword),
                "ElonPostRuleState")
            .AddAttributeLists(
                Attribute("GenerateSerializer"),
                Attribute("Alias", StringLiteral(LocalAlias(intent, "state"))))
            .AddModifiers(Public(), Sealed())
            .WithParameterList(SyntaxFactory.ParameterList(
                SyntaxFactory.SingletonSeparatedList(
                    SerializedParameter(
                        "AcceptedCount",
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                        0))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    private static ClassDeclarationSyntax CreateRuleNeuron(ElonChartAuthoringIntent intent)
    {
        var stateType = SyntaxFactory.IdentifierName("ElonPostRuleState");
        var constructor = SyntaxFactory.ConstructorDeclaration("ElonPostRuleNeuron")
            .AddModifiers(Public())
            .AddParameterListParameters(
                Parameter("digitalBrain", SyntaxFactory.IdentifierName("IDigitalBrain")),
                Parameter(
                    "durableState",
                    Generic("IDurableState", stateType)))
            .WithInitializer(SyntaxFactory.ConstructorInitializer(
                SyntaxKind.BaseConstructorInitializer,
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                [
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("digitalBrain")),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("durableState")),
                ]))))
            .WithBody(SyntaxFactory.Block());

        var author = Member(Identifier("synapse"), "Author");
        var authorMatches = Invoke(
            Member(NameExpression("System", "String"), "Equals"),
            author,
            StringLiteral(intent.ExpectedAuthor),
            Member(NameExpression("System", "StringComparison"), "OrdinalIgnoreCase"));
        var earlyReturn = SyntaxFactory.IfStatement(
            SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, authorMatches),
            SyntaxFactory.ReturnStatement(Member(NameExpression("Task"), "CompletedTask")));

        var acceptedCount = Member(Member(Identifier("DurableState"), "Value"), "AcceptedCount");
        var nextAcceptedCount = SyntaxFactory.BinaryExpression(
            SyntaxKind.AddExpression,
            acceptedCount,
            NumericLiteral(1));
        var nextState = SyntaxFactory.ObjectCreationExpression(stateType)
            .WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(nextAcceptedCount))));
        var replaceState = SyntaxFactory.ExpressionStatement(
            Invoke(Member(Identifier("DurableState"), "Replace"), nextState));
        var matched = SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.IdentifierName("ElonPostMatched"))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Argument(Member(Identifier("synapse"), "PostId")),
                SyntaxFactory.Argument(Member(Identifier("synapse"), "OccurredAt")),
                SyntaxFactory.Argument(nextAcceptedCount),
            ])));
        var fire = SyntaxFactory.ReturnStatement(Invoke(
            Member(Identifier("DigitalBrain"), "FireSynapse"),
            matched,
            Identifier("cancellationToken")));

        var handler = HandlerMethod("SocialPostObserved", earlyReturn, replaceState, fire);
        return SyntaxFactory.ClassDeclaration("ElonPostRuleNeuron")
            .AddModifiers(Public(), Sealed())
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SeparatedList<BaseTypeSyntax>(
            [
                SyntaxFactory.SimpleBaseType(Generic("Neuron", stateType)),
                SyntaxFactory.SimpleBaseType(Generic(
                    "IHandle",
                    SyntaxFactory.IdentifierName("SocialPostObserved"))),
            ])))
            .AddMembers(constructor, handler);
    }

    private static ClassDeclarationSyntax CreateForwarderNeuron(ElonChartAuthoringIntent intent)
    {
        var constructor = SyntaxFactory.ConstructorDeclaration("ChartForwarderNeuron")
            .AddModifiers(Public())
            .AddParameterListParameters(
                Parameter("digitalBrain", SyntaxFactory.IdentifierName("IDigitalBrain")))
            .WithInitializer(SyntaxFactory.ConstructorInitializer(
                SyntaxKind.BaseConstructorInitializer,
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(Identifier("digitalBrain"))))))
            .WithBody(SyntaxFactory.Block());
        var draft = SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("ChartPointDraft"))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Argument(Member(Identifier("synapse"), "PostId")),
                SyntaxFactory.Argument(Member(Identifier("synapse"), "OccurredAt")),
            ])));
        var command = SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("AddChartPoint"))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Argument(StringLiteral(intent.ChartId)),
                SyntaxFactory.Argument(draft),
            ])));
        var handler = HandlerMethod(
            "ElonPostMatched",
            SyntaxFactory.ReturnStatement(Invoke(
                Member(Identifier("DigitalBrain"), "FireSynapse"),
                command,
                Identifier("cancellationToken"))));

        return SyntaxFactory.ClassDeclaration("ChartForwarderNeuron")
            .AddModifiers(Public(), Sealed())
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SeparatedList<BaseTypeSyntax>(
            [
                SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("Neuron")),
                SyntaxFactory.SimpleBaseType(Generic(
                    "IHandle",
                    SyntaxFactory.IdentifierName("ElonPostMatched"))),
            ])))
            .AddMembers(constructor, handler);
    }

    private static MethodDeclarationSyntax HandlerMethod(
        string synapseType,
        params StatementSyntax[] statements) =>
        SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName("Task"), "HandleAsync")
            .AddModifiers(Public())
            .AddParameterListParameters(
                Parameter("synapse", SyntaxFactory.IdentifierName(synapseType)),
                Parameter("cancellationToken", SyntaxFactory.IdentifierName("CancellationToken")))
            .WithBody(SyntaxFactory.Block(statements));

    private static ParameterSyntax SerializedParameter(string name, TypeSyntax type, int id) =>
        Parameter(name, type).AddAttributeLists(
            Attribute("Id", NumericLiteral(id)).WithTarget(
                SyntaxFactory.AttributeTargetSpecifier(
                    SyntaxFactory.Token(SyntaxKind.PropertyKeyword))));

    private static ParameterSyntax Parameter(string name, TypeSyntax type) =>
        SyntaxFactory.Parameter(SyntaxFactory.Identifier(name)).WithType(type);

    private static AttributeListSyntax Attribute(string name, ExpressionSyntax? argument = null)
    {
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(name));
        if (argument is not null)
        {
            attribute = attribute.WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(argument))));
        }

        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
    }

    private static UsingDirectiveSyntax Using(params string[] parts) =>
        SyntaxFactory.UsingDirective(Name(parts));

    private static NameSyntax Name(params string[] parts)
    {
        NameSyntax name = SyntaxFactory.IdentifierName(parts[0]);
        for (var index = 1; index < parts.Length; index++)
        {
            name = SyntaxFactory.QualifiedName(name, SyntaxFactory.IdentifierName(parts[index]));
        }

        return name;
    }

    private static ExpressionSyntax NameExpression(params string[] parts)
    {
        ExpressionSyntax expression = Identifier(parts[0]);
        for (var index = 1; index < parts.Length; index++)
        {
            expression = Member(expression, parts[index]);
        }

        return expression;
    }

    private static GenericNameSyntax Generic(string name, params TypeSyntax[] arguments) =>
        SyntaxFactory.GenericName(name)
            .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SeparatedList(arguments)));

    private static InvocationExpressionSyntax Invoke(
        ExpressionSyntax target,
        params ExpressionSyntax[] arguments) =>
        SyntaxFactory.InvocationExpression(
            target,
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                arguments.Select(SyntaxFactory.Argument))));

    private static MemberAccessExpressionSyntax Member(ExpressionSyntax target, string member) =>
        SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            target,
            SyntaxFactory.IdentifierName(member));

    private static IdentifierNameSyntax Identifier(string name) => SyntaxFactory.IdentifierName(name);

    private static LiteralExpressionSyntax StringLiteral(string value) =>
        SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(value));

    private static LiteralExpressionSyntax NumericLiteral(int value) =>
        SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(value));

    private static SyntaxToken Public() => SyntaxFactory.Token(SyntaxKind.PublicKeyword);

    private static SyntaxToken Sealed() => SyntaxFactory.Token(SyntaxKind.SealedKeyword);

    private static string LocalAlias(ElonChartAuthoringIntent intent, string name) =>
        $"db.poc.family.{intent.Family.Value}.{name}.v{intent.LocalSynapseSchemaVersion}";
}
