using DigitalBrain.Analyzers.Apps;
using DigitalBrain.Analyzers.CodeFixes;

namespace DigitalBrain.Analyzers.Tests.Unit.Apps.Fixes;

// 08-05 (R4-07): the fixes that rewrite to App base members. Every Apply checks that the fixed code compiles; each fact checks the rule stops reporting.
public sealed class KeyCompositionFixFacts
{
    [Fact]
    public async Task A_concatenated_string_key_becomes_parsed_typed_parts()
    {
        var (text, _, after) = await FixRunner.Apply(new KeyCompositionAnalyzer(), new KeyCompositionFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                public string View(string appKey, int i) => appKey + "/slot/" + i;
            }
            """);
        Assert.Contains("public string View(string appKey, int i) => AppKey.Parse(appKey) / \"slot\" / i;", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task An_interpolated_typed_key_keeps_its_type_and_mixed_parts_stay_interpolated()
    {
        var (text, _, after) = await FixRunner.Apply(new KeyCompositionAnalyzer(), new KeyCompositionFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                public string View(BrainId brain, int i)
                {
                    var rootKey = Key(brain);
                    return $"{rootKey}/view/slot-{i}";
                }
            }
            """);
        Assert.Contains("return rootKey / \"view\" / $\"slot-{i}\";", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task String_Concat_is_rewritten_too()
    {
        var (text, _, after) = await FixRunner.Apply(new KeyCompositionAnalyzer(), new KeyCompositionFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                public string View(string appKey, string name) => string.Concat(appKey, "/files/", name);
            }
            """);
        Assert.Contains("=> AppKey.Parse(appKey) / \"files\" / name;", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task No_fix_when_the_result_must_stay_a_string()
    {
        var offered = await FixRunner.Offered(new KeyCompositionAnalyzer(), new KeyCompositionFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                public int Length(string appKey) => (appKey + "/view").Length;
            }
            """);
        Assert.Empty(offered);
    }

    [Fact]
    public async Task No_fix_when_the_key_is_not_first()
    {
        var offered = await FixRunner.Offered(new KeyCompositionAnalyzer(), new KeyCompositionFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                public string View(string appKey) => "root/" + appKey;
            }
            """);
        Assert.Empty(offered);
    }
}

public sealed class BaseBoilerplateFixFacts
{
    [Fact]
    public async Task A_re_implemented_Key_method_is_removed_and_callers_use_the_base()
    {
        var (text, _, after) = await FixRunner.Apply(new BaseBoilerplateAnalyzer(), new BaseBoilerplateFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                private string Key(BrainId brain) => "a";
                public string View(BrainId brain) => Key(brain);
            }
            """);
        Assert.DoesNotContain("=> \"a\"", text, StringComparison.Ordinal);
        Assert.Contains("public string View(BrainId brain) => Key(brain);", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task A_nested_request_ledger_is_removed_and_its_uses_become_the_base_type()
    {
        var (text, _, after) = await FixRunner.Apply(new BaseBoilerplateAnalyzer(), new BaseBoilerplateFix(), """
            using System;
            using DigitalBrain;
            public sealed class A : App
            {
                private readonly FilesRequestLedger _ledger = new FilesRequestLedger();
                public bool Seen(Guid id) => _ledger.Handled(id);
                private sealed class FilesRequestLedger
                {
                    public bool Handled(Guid id) => false;
                }
            }
            """);
        Assert.DoesNotContain("FilesRequestLedger", text, StringComparison.Ordinal);
        Assert.Contains("private readonly RequestLedger _ledger = new RequestLedger();", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task A_Present_wrapper_with_the_base_shape_is_removed()
    {
        var (text, _, after) = await FixRunner.Apply(new BaseBoilerplateAnalyzer(), new BaseBoilerplateFix(), """
            using System;
            using DigitalBrain;
            public sealed class A : App
            {
                private bool Present<TView>(Action<TView, TView> publish, Func<TView> side, Func<TView> main, string operation = "") where TView : class
                {
                    publish(side(), main());
                    return true;
                }
                public void Show(Action<string, string> publish) => Present(publish, () => "s", () => "m");
            }
            """);
        Assert.DoesNotContain("publish(side(), main())", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task No_fix_when_callers_need_another_shape()
    {
        var offered = await FixRunner.Offered(new BaseBoilerplateAnalyzer(), new BaseBoilerplateFix(), """
            using System.Threading.Tasks;
            using DigitalBrain;
            public sealed class A : App
            {
                public Task Present() => Task.CompletedTask;
                public Task Open() => Present();
            }
            """);
        Assert.Empty(offered);
    }
}

public sealed class RecordErrorFixFacts
{
    [Fact]
    public async Task A_typed_catch_gets_a_variable_and_records_the_error_state()
    {
        var (text, _, after) = await FixRunner.Apply(new EmptyCatchAnalyzer(), new EmptyCatchFix(), key: EmptyCatchFix.RecordErrorKey, source: """
            using System;
            using System.IO;
            using DigitalBrain;
            public sealed class A : App
            {
                public void Run()
                {
                    try { Console.WriteLine(); }
                    catch (IOException) { return; }
                }
            }
            """);
        Assert.Contains("catch (IOException error) { RecordError(error); return; }", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task A_bare_catch_catches_Exception_and_keeps_an_existing_variable_name_free()
    {
        var (text, _, after) = await FixRunner.Apply(new EmptyCatchAnalyzer(), new EmptyCatchFix(), key: EmptyCatchFix.RecordErrorKey, source: """
            using System;
            using DigitalBrain;
            public sealed class A : App
            {
                public void Run(string error)
                {
                    try { Console.WriteLine(error); }
                    catch { }
                }
            }
            """);
        Assert.Contains("catch (Exception error2)", text, StringComparison.Ordinal);
        Assert.Contains("RecordError(error2);", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Fix_all_records_every_site()
    {
        var (text, before, after) = await FixRunner.Apply(new EmptyCatchAnalyzer(), new EmptyCatchFix(), fixAll: true, key: EmptyCatchFix.RecordErrorKey, source: """
            using System;
            using DigitalBrain;
            public sealed class A : App
            {
                public void One() { try { } catch { } }
                public void Two() { try { } catch (Exception e) { } }
            }
            """);
        Assert.Equal(2, before.Length);
        Assert.Contains("RecordError(error);", text, StringComparison.Ordinal);
        Assert.Contains("RecordError(e);", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Only_rethrow_is_offered_in_a_static_member_or_an_attribute_only_app()
    {
        var inStatic = await FixRunner.Offered(new EmptyCatchAnalyzer(), new EmptyCatchFix(), """
            using DigitalBrain;
            public sealed class A : App
            {
                public static void Run() { try { } catch { } }
            }
            """);
        Assert.Equal([EmptyCatchFix.Key], inStatic);

        var noBase = await FixRunner.Offered(new EmptyCatchAnalyzer(), new EmptyCatchFix(), """
            using DigitalBrain;
            [App("a")] public sealed class A
            {
                public void Run() { try { } catch { } }
            }
            """);
        Assert.Equal([EmptyCatchFix.Key], noBase);
    }
}
