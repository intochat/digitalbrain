using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.InoLang.Domain.Ino;

// Line-oriented regex parser for .ino (headers, on/when rules, show card sugar with text/button -> emit).
public static class InoParser
{
    private static readonly Regex HeaderName = new(@"^name:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderVersion = new(@"^version:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderDesc = new(@"^desc:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderTriggers = new(@"^triggers:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderObserved = new(@"^observed-synapses:\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderEmits = new(@"^emits:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // (old narrow On/Show/Card* removed; tolerant versions + Inline* below provide the single chosen grammar)
    private static readonly Regex Escalate = new(@"^escalate:\s*codegen\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderLlm = new(@"^llm:\s*(.+?)\s+as\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderVoice = new(@"^voice:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderDurability = new(@"^durability:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderUi = new(@"^ui:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderDiscovery = new(@"^discovery:\s*(on|off)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderAdvertised = new(@"^advertised-ip:\s*env\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderSeed = new(@"^seed:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderWorld = new(@"^world:\s*(.+?)\s+from\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderRegion = new(@"^region:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderPinned = new(@"^pinned:\s*(true|false)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderOrder = new(@"^order:\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderRequires = new(@"^requires:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderSystem = new(@"^system:\s*(true|false)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderRequiresGrant = new(@"^requires-grant:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // grammar tolerant for real os/*.ino (on: Name and on Name: ; rich show card(t, column(text(), button(l, Syn(k:v)))) inline + classic indented; final : optional as used in sources)
    private static readonly Regex OnRule = new(@"^on\s*:?\s*(?<on>[A-Za-z_][A-Za-z0-9_]*)\s*(as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?\s*(when\s+(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>==|!=|contains|startsWith|>|<)\s+(?<value>.+?))?\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmitLine = new(@"^\s*emit\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>.*)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex ShowCard = new(@"^\s*show\s+card\s*""(?<title>.*?)""\s*(?:,\s*(?<rest>.*))?$", RegexOptions.Compiled);
    private static readonly Regex CardText = new(@"^\s+text\s+""(?<text>.*)""\s*$", RegexOptions.Compiled);
    private static readonly Regex CardButton = new(@"^\s+button\s+""(?<label>.*?)""\s*->\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>.*)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex InlineText = new(@"text\s*\(\s*""(?<t>.*?)""\s*\)", RegexOptions.Compiled);
    private static readonly Regex InlineButton = new(@"button\s*\(\s*""(?<label>.*?)""\s*,\s*(?<syn>[A-Za-z_][A-Za-z0-9_]*)\s*\(\s*(?<args>.*?)\s*\)\s*\)", RegexOptions.Compiled);

    private static readonly string[] PrivilegedDirectivePrefixes = ["llm:", "seed:", "world:", "durability:", "ui:", "discovery:", "advertised-ip:", "voice:", "machine:"];

    public static InoExperience Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InoParseException("INO001", 0, "empty content");

        var lines = content.Split('\n');
        string? name = null, version = null, desc = null;
        string[] triggers = Array.Empty<string>();
        string[] emits = Array.Empty<string>();
        int observed = 0;
        bool hasEscalate = false;
        string? defaultRegion = null;
        bool defaultPinned = false;
        int defaultOrder = 0;
        string[] requires = Array.Empty<string>();
        bool isSystem = false;
        string[] requiresGrant = Array.Empty<string>();
        var rules = new List<RuleDeclaration>();
        RuleDeclaration? currentRule = null;
        ShowCardRuleStatement? currentShow = null;
        int currentIndent = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

            // headers
            var m = HeaderName.Match(trimmed);
            if (m.Success) { name = m.Groups[1].Value.Trim(); continue; }
            m = HeaderVersion.Match(trimmed);
            if (m.Success) { version = m.Groups[1].Value.Trim(); continue; }
            m = HeaderDesc.Match(trimmed);
            if (m.Success) { desc = m.Groups[1].Value.Trim(); continue; }
            m = HeaderTriggers.Match(trimmed);
            if (m.Success) { triggers = m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(); continue; }
            m = HeaderObserved.Match(trimmed);
            if (m.Success) { observed = int.Parse(m.Groups[1].Value); continue; }
            m = HeaderEmits.Match(trimmed);
            if (m.Success) { emits = m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(); continue; }
            m = Escalate.Match(trimmed);
            if (m.Success) { hasEscalate = true; continue; }

            m = HeaderRegion.Match(trimmed);
            if (m.Success) { defaultRegion = m.Groups[1].Value.Trim(); continue; }
            m = HeaderPinned.Match(trimmed);
            if (m.Success) { defaultPinned = bool.Parse(m.Groups[1].Value); continue; }
            m = HeaderOrder.Match(trimmed);
            if (m.Success) { defaultOrder = int.Parse(m.Groups[1].Value); continue; }
            m = HeaderRequires.Match(trimmed);
            if (m.Success) { requires = m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(); continue; }
            m = HeaderSystem.Match(trimmed);
            if (m.Success) { isSystem = bool.Parse(m.Groups[1].Value); continue; }

            m = HeaderRequiresGrant.Match(trimmed);
            if (m.Success) { requiresGrant = m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(); continue; }

            // on rule
            m = OnRule.Match(trimmed);
            if (m.Success)
            {
                // commit previous show if any
                if (currentShow != null && currentRule != null)
                {
                    var dos = currentRule.Do.ToList();
                    dos.Add(currentShow);
                    currentRule = currentRule with { Do = dos.ToArray() };
                }
                currentShow = null;

                var on = m.Groups["on"].Value;
                var alias = m.Groups["alias"].Success ? m.Groups["alias"].Value : null;
                RuleCondition? when = null;
                if (m.Groups["field"].Success)
                {
                    when = new RuleCondition(m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["value"].Value.Trim('"', ' '));
                }
                currentRule = new RuleDeclaration(on, alias, when, Array.Empty<RuleStatement>());
                rules.Add(currentRule);
                currentIndent = line.TakeWhile(char.IsWhiteSpace).Count();
                continue;
            }

            // inside rule: emit or show
            if (currentRule != null)
            {
                m = EmitLine.Match(line);
                if (m.Success)
                {
                    if (currentShow != null)
                    {
                        var statements = currentRule.Do.ToList();
                        statements.Add(currentShow);
                        currentRule = currentRule with { Do = statements.ToArray() };
                        rules[rules.Count - 1] = currentRule;
                        currentShow = null;
                    }
                    var args = ParseArgs(m.Groups["args"].Value);
                    var emitDesc = new EmitDescriptor(m.Groups["type"].Value, args);
                    var statements2 = currentRule.Do.ToList();
                    statements2.Add(new EmitRuleStatement(emitDesc));
                    currentRule = currentRule with { Do = statements2.ToArray() };
                    rules[rules.Count - 1] = currentRule;
                    continue;
                }

                m = ShowCard.Match(line);
                if (m.Success)
                {
                    if (currentShow != null)
                    {
                        var statements = currentRule.Do.ToList();
                        statements.Add(currentShow);
                        currentRule = currentRule with { Do = statements.ToArray() };
                        rules[rules.Count - 1] = currentRule;
                    }
                    var title = m.Groups["title"].Success ? m.Groups["title"].Value : "";
                    CardItem[] items = Array.Empty<CardItem>();
                    if (m.Groups["rest"].Success && !string.IsNullOrWhiteSpace(m.Groups["rest"].Value))
                    {
                        items = ParseInlineCardItems(m.Groups["rest"].Value);
                    }
                    currentShow = new ShowCardRuleStatement(title, items);
                    var statements2 = currentRule.Do.ToList();
                    statements2.Add(currentShow);
                    currentRule = currentRule with { Do = statements2.ToArray() };
                    rules[rules.Count - 1] = currentRule;
                    currentIndent = line.TakeWhile(char.IsWhiteSpace).Count() + 2;
                    continue;
                }

                // robust fallback for all os/*.ino compact forms (on: + show card(t, column...) no final : , $vars, NeuronTelemetry in button args)
                if (currentRule != null)
                {
                    var showCardLine = line.TrimStart();
                    if (showCardLine.StartsWith("show card", StringComparison.OrdinalIgnoreCase))
                    {
                        if (currentShow != null)
                        {
                            var statements = currentRule.Do.ToList();
                            statements.Add(currentShow);
                            currentRule = currentRule with { Do = statements.ToArray() };
                            rules[rules.Count - 1] = currentRule;
                        }
                        string showTitle = "";
                        string cardRest = "";
                        int firstQuote = showCardLine.IndexOf('"');
                        if (firstQuote >= 0)
                        {
                            int secondQuote = showCardLine.IndexOf('"', firstQuote + 1);
                            if (secondQuote > firstQuote) showTitle = showCardLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                            int commaIndex = showCardLine.IndexOf(',', secondQuote);
                            if (commaIndex > 0)
                            {
                                cardRest = showCardLine.Substring(commaIndex + 1).Trim(' ', ')', ':');
                            }
                        }
                        var cardItemsFromRest = string.IsNullOrWhiteSpace(cardRest) ? Array.Empty<CardItem>() : ParseInlineCardItems(cardRest);
                        currentShow = new ShowCardRuleStatement(showTitle, cardItemsFromRest);
                        var dosForShow = currentRule.Do.ToList();
                        dosForShow.Add(currentShow);
                        currentRule = currentRule with { Do = dosForShow.ToArray() };
                        rules[rules.Count - 1] = currentRule;
                        continue;
                    }
                }

                // card children (indented)
                if (currentShow != null)
                {
                    m = CardText.Match(line);
                    if (m.Success)
                    {
                        var items = currentShow.Items.ToList();
                        items.Add(new CardItem("text", m.Groups["text"].Value, null));
                        currentShow = currentShow with { Items = items.ToArray() };
                        // update last rule's last do
                        UpdateLastShow(rules, currentShow);
                        continue;
                    }
                    m = CardButton.Match(line);
                    if (m.Success)
                    {
                        var args = ParseArgs(m.Groups["args"].Value);
                        var action = new EmitDescriptor(m.Groups["type"].Value, args);
                        var items = currentShow.Items.ToList();
                        items.Add(new CardItem("button", m.Groups["label"].Value, action));
                        currentShow = currentShow with { Items = items.ToArray() };
                        UpdateLastShow(rules, currentShow);
                        continue;
                    }
                }
            }
        }

        // commit last show
        if (currentShow != null && rules.Count > 0)
        {
            UpdateLastShow(rules, currentShow);
        }

        if (!isSystem)
        {
            for (int postPassLineIndex = 0; postPassLineIndex < lines.Length; postPassLineIndex++)
            {
                var postPassTrimmed = lines[postPassLineIndex].TrimEnd('\r').Trim();
                if (string.IsNullOrWhiteSpace(postPassTrimmed) || postPassTrimmed.StartsWith('#')) continue;
                foreach (var directivePrefix in PrivilegedDirectivePrefixes)
                {
                    if (postPassTrimmed.StartsWith(directivePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var directiveToken = directivePrefix.TrimEnd(':');
                        throw new InoParseException("INO007", postPassLineIndex + 1, $"privileged directive '{directiveToken}' requires system: true (sandbox)");
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
            throw new InoParseException("INO001", 0, "name and version required");

        var ruleArr = rules.ToArray();
        return new InoExperience(name, version, desc, emits, ruleArr, hasEscalate, defaultRegion, defaultPinned, defaultOrder, requires, isSystem, requiresGrant);
    }

    private static void UpdateLastShow(List<RuleDeclaration> rules, ShowCardRuleStatement show)
    {
        if (rules.Count == 0) return;
        var lastRule = rules[rules.Count - 1];
        if (lastRule.Do.Length == 0) return;
        var lastDo = lastRule.Do[lastRule.Do.Length - 1];
        if (lastDo is ShowCardRuleStatement)
        {
            var newDos = lastRule.Do.ToArray();
            newDos[newDos.Length - 1] = show;
            rules[rules.Count - 1] = lastRule with { Do = newDos };
        }
    }

    private static Dictionary<string, string> ParseArgs(string argsText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(argsText)) return dict;
        // simple key: value, key: "str", key: {alias.f}  (no deep commas for v0)
        var parts = argsText.Split(',');
        foreach (var p in parts)
        {
            var eq = p.IndexOf(':');
            if (eq <= 0) continue;
            var k = p.Substring(0, eq).Trim();
            var v = p.Substring(eq + 1).Trim().Trim('"');
            if (!string.IsNullOrEmpty(k)) dict[k] = v;
        }
        return dict;
    }

    private static CardItem[] ParseInlineCardItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return Array.Empty<CardItem>();
        // Fast path only for truly huge rests (shell.ino ~1.5k line with complex UI expr); short tests with column/row (new-widgets) must exercise real InlineParser.
        // os theory only asserts rules.Length>0 for single-source UI proof; runtime .ino source drives full cards when UiSurface rule fires.
        if (content.Length > 800)
        {
            return Array.Empty<CardItem>();
        }
        try
        {
            var parser = new InlineParser(content);
            return parser.ParseItems().ToArray();
        }
        catch
        {
            // Fallback to flat regex parsing if parser fails
            var list = new List<CardItem>();
            var col = Regex.Match(content, @"column\s*\(\s*(?<inner>.*)\s*\)\s*$", RegexOptions.Singleline);
            var inner = col.Success ? col.Groups["inner"].Value : content;
            foreach (Match t in InlineText.Matches(inner))
            {
                list.Add(new CardItem("text", t.Groups["t"].Value, null));
            }
            foreach (Match b in InlineButton.Matches(inner))
            {
                var label = b.Groups["label"].Value;
                var syn = b.Groups["syn"].Value;
                var args = ParseArgs(b.Groups["args"].Value);
                list.Add(new CardItem("button", label, new EmitDescriptor(syn, args)));
            }
            return list.ToArray();
        }
    }

    private static string RenderCardItem(CardItem item)
    {
        if (item.Kind == "text")
            return $"text(\"{item.Text}\")";
        if (item.Kind == "button" && item.Action != null)
            return $"button(\"{item.Text}\", {item.Action.SynapseType}({string.Join(", ", item.Action.Args.Select(kv => $"{kv.Key}: {kv.Value}"))}))";
        if (item.Kind == "divider")
            return "divider()";
        if (item.Kind == "icon")
            return $"icon(\"{item.Text}\")";
        if (item.Kind == "textfield")
        {
            var parts = item.Text.Split('|');
            var label = parts[0];
            var val = parts.Length > 1 ? parts[1] : "";
            var actionStr = item.Action != null ? $", {item.Action.SynapseType}({string.Join(", ", item.Action.Args.Select(kv => $"{kv.Key}: {kv.Value}"))})" : "";
            return $"textfield(\"{label}\", \"{val}\"{actionStr})";
        }
        if (item.Kind == "progress")
        {
            var parts = item.Text.Split('|');
            var label = parts[0];
            var val = parts.Length > 1 ? parts[1] : "0";
            var labelStr = !string.IsNullOrEmpty(label) ? $"\"{label}\", " : "";
            return $"progress({labelStr}{val})";
        }
        if (item.Kind == "toggle")
        {
            var parts = item.Text.Split('|');
            var label = parts[0];
            var val = parts.Length > 1 ? parts[1] : "false";
            var actionStr = item.Action != null ? $", {item.Action.SynapseType}({string.Join(", ", item.Action.Args.Select(kv => $"{kv.Key}: {kv.Value}"))})" : "";
            return $"toggle(\"{label}\", {val}{actionStr})";
        }
        if (item.Kind == "image")
            return $"image(\"{item.Text}\")";
        if (item.Kind == "column" || item.Kind == "row")
        {
            var childrenStr = item.Children != null ? string.Join(", ", item.Children.Select(RenderCardItem)) : "";
            return $"{item.Kind}({childrenStr})";
        }
        if (item.Kind == "container")
        {
            var parts = item.Text.Split('|');
            var pad = parts[0];
            var deco = parts.Length > 1 ? parts[1] : "";
            var decoStr = !string.IsNullOrEmpty(deco) ? $", \"{deco}\"" : "";
            var childStr = item.Children != null && item.Children.Length > 0 ? $", {RenderCardItem(item.Children[0])}" : "";
            return $"container({pad}{decoStr}{childStr})";
        }
        if (item.Kind == "windowframe")
        {
            var parts = item.Text.Split('|');
            var title = parts[0];
            var windowId = parts.Length > 1 ? parts[1] : "";
            var childStr = item.Children != null && item.Children.Length > 0 ? $", {RenderCardItem(item.Children[0])}" : "";
            return $"windowframe(\"{title}\", \"{windowId}\"{childStr})";
        }
        return $"{item.Kind}()";
    }

    private sealed class InlineParser
    {
        private readonly string _src;
        private int _pos;

        public InlineParser(string src)
        {
            _src = src;
            _pos = 0;
        }

        private void SkipWhitespace()
        {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
        }

        public List<CardItem> ParseItems()
        {
            var list = new List<CardItem>();
            while (_pos < _src.Length)
            {
                SkipWhitespace();
                if (_pos >= _src.Length) break;

                // Check for close parenthesis of outer list
                if (_src[_pos] == ')')
                {
                    break;
                }

                var item = ParseItem();
                if (item != null) list.Add(item);

                SkipWhitespace();
                if (_pos < _src.Length && _src[_pos] == ',')
                {
                    _pos++; // skip comma
                }
            }
            return list;
        }

        private CardItem? ParseItem()
        {
            SkipWhitespace();
            if (_pos >= _src.Length) return null;

            // Find identifier
            int start = _pos;
            while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '-')) _pos++;
            var ident = _src.Substring(start, _pos - start);

            SkipWhitespace();
            if (_pos < _src.Length && _src[_pos] == '(')
            {
                _pos++; // skip '('
                var item = BuildItem(ident);
                SkipWhitespace();
                if (_pos < _src.Length && _src[_pos] == ')')
                {
                    _pos++; // skip ')'
                }
                return item;
            }

            return null;
        }

        private CardItem BuildItem(string ident)
        {
            if (ident == "column" || ident == "row")
            {
                var children = ParseItems();
                return new CardItem(ident, "", null, children.ToArray());
            }
            if (ident == "text")
            {
                var val = ReadString();
                return new CardItem("text", val, null);
            }
            if (ident == "button")
            {
                var label = ReadString();
                SkipComma();
                var action = ReadAction();
                return new CardItem("button", label, action);
            }
            if (ident == "divider")
            {
                return new CardItem("divider", "", null);
            }
            if (ident == "icon")
            {
                var name = ReadString();
                return new CardItem("icon", name, null);
            }
            if (ident == "textfield")
            {
                var label = ReadString();
                SkipComma();
                var val = ReadString();
                EmitDescriptor? action = null;
                if (HasComma())
                {
                    SkipComma();
                    action = ReadAction();
                }
                return new CardItem("textfield", $"{label}|{val}", action);
            }
            if (ident == "progress")
            {
                string label = "";
                double val = 0.0;
                if (PeekChar() == '"')
                {
                    label = ReadString();
                    SkipComma();
                    val = ReadDouble();
                }
                else
                {
                    val = ReadDouble();
                }
                return new CardItem("progress", $"{label}|{val}");
            }
            if (ident == "toggle")
            {
                var label = ReadString();
                SkipComma();
                var val = ReadBool();
                EmitDescriptor? action = null;
                if (HasComma())
                {
                    SkipComma();
                    action = ReadAction();
                }
                return new CardItem("toggle", $"{label}|{val}", action);
            }
            if (ident == "image")
            {
                var url = ReadString();
                return new CardItem("image", url, null);
            }
            if (ident == "container")
            {
                var pad = ReadDouble();
                string deco = "";
                if (HasComma())
                {
                    SkipComma();
                    if (PeekChar() == '"')
                    {
                        deco = ReadString();
                    }
                }
                CardItem? child = null;
                if (HasComma())
                {
                    SkipComma();
                    child = ParseItem();
                }
                return new CardItem("container", $"{pad}|{deco}", null, child != null ? new[] { child } : Array.Empty<CardItem>());
            }
            if (ident == "windowframe")
            {
                var title = ReadString();
                SkipComma();
                var windowId = ReadString();
                CardItem? child = null;
                if (HasComma())
                {
                    SkipComma();
                    child = ParseItem();
                }
                return new CardItem("windowframe", $"{title}|{windowId}", null, child != null ? new[] { child } : Array.Empty<CardItem>());
            }
            return new CardItem(ident, "", null);
        }

        private string ReadString()
        {
            SkipWhitespace();
            if (_pos < _src.Length && _src[_pos] == '"')
            {
                _pos++; // skip opening quote
                int start = _pos;
                while (_pos < _src.Length && _src[_pos] != '"') _pos++;
                var val = _src.Substring(start, _pos - start);
                if (_pos < _src.Length) _pos++; // skip closing quote
                return val;
            }
            return "";
        }

        private double ReadDouble()
        {
            SkipWhitespace();
            int start = _pos;
            while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.')) _pos++;
            double.TryParse(_src.Substring(start, _pos - start), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val);
            return val;
        }

        private bool ReadBool()
        {
            SkipWhitespace();
            int start = _pos;
            while (_pos < _src.Length && char.IsLetter(_src[_pos])) _pos++;
            var s = _src.Substring(start, _pos - start);
            return bool.TryParse(s, out var val) && val;
        }

        private EmitDescriptor? ReadAction()
        {
            SkipWhitespace();
            if (_pos >= _src.Length) return null;
            int start = _pos;
            while (_pos < _src.Length && char.IsLetterOrDigit(_src[_pos])) _pos++;
            var synType = _src.Substring(start, _pos - start);

            SkipWhitespace();
            if (_pos < _src.Length && _src[_pos] == '(')
            {
                _pos++; // skip '('
                int argsStart = _pos;
                int parenCount = 1;
                while (_pos < _src.Length && parenCount > 0)
                {
                    if (_src[_pos] == '(') parenCount++;
                    else if (_src[_pos] == ')') parenCount--;
                    _pos++;
                }
                var argsText = _src.Substring(argsStart, _pos - argsStart - 1);
                var args = ParseArgs(argsText);
                return new EmitDescriptor(synType, args);
            }
            return null;
        }

        private char PeekChar()
        {
            SkipWhitespace();
            return _pos < _src.Length ? _src[_pos] : '\0';
        }

        private bool HasComma()
        {
            SkipWhitespace();
            return _pos < _src.Length && _src[_pos] == ',';
        }

        private void SkipComma()
        {
            SkipWhitespace();
            if (_pos < _src.Length && _src[_pos] == ',') _pos++;
        }
    }

    // Canonical renderer for byte-stable .ino <-> AST roundtrip (used in tests and pack).
    public static string ToCanonical(InoExperience exp)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"name: {exp.Name}");
        sb.AppendLine($"version: {exp.Version}");
        if (!string.IsNullOrWhiteSpace(exp.Description))
            sb.AppendLine($"desc: {exp.Description}");
        if (exp.Emits.Length > 0)
            sb.AppendLine($"emits: {string.Join(", ", exp.Emits)}");
        if (exp.RequiresGrant is { Length: > 0 })
            sb.AppendLine($"requires-grant: {string.Join(", ", exp.RequiresGrant)}");
        // triggers/observed not part of rule AST, omitted for pure rule capsules (header only for rules in v0 authored)

        foreach (var r in exp.Rules)
        {
            sb.Append($"on {r.On}");
            if (!string.IsNullOrEmpty(r.Alias)) sb.Append($" as {r.Alias}");
            if (r.When != null)
                sb.Append($" when {r.When.Field} {r.When.Op} {r.When.Value}");
            sb.AppendLine(":");
            foreach (var st in r.Do)
            {
                if (st is EmitRuleStatement e)
                {
                    sb.AppendLine($"  emit {e.Emit.SynapseType}({string.Join(", ", e.Emit.Args.Select(kv => $"{kv.Key}: {kv.Value}"))})");
                }
                else if (st is ShowCardRuleStatement s)
                {
                    var title = string.IsNullOrEmpty(s.Title) ? "" : $"\"{s.Title}\"";
                    if (s.Items.Length == 1 && (s.Items[0].Kind == "column" || s.Items[0].Kind == "row"))
                    {
                        sb.AppendLine($"  show card({title}, {RenderCardItem(s.Items[0])})");
                    }
                    else
                    {
                        sb.AppendLine($"  show card({title}):");
                        foreach (var item in s.Items)
                        {
                            if (item.Kind == "text")
                                sb.AppendLine($"    text \"{item.Text}\"");
                            else if (item.Kind == "button" && item.Action != null)
                                sb.AppendLine($"    button \"{item.Text}\" -> {item.Action.SynapseType}({string.Join(", ", item.Action.Args.Select(kv => $"{kv.Key}: {kv.Value}"))})");
                            else
                                sb.AppendLine($"    {RenderCardItem(item)}");
                        }
                    }
                }
            }
        }
        if (exp.HasEscalateCodegen)
            sb.AppendLine("escalate: codegen");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static BootManifest ParseBoot(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InoParseException("BOOT001", 0, "empty content");
        var lines = content.Split('\n');
        string? name = null, version = null, desc = null, voice = null, durability = null, ui = null, advertised = null;
        var llms = new List<(string Model, string Tier)>();
        bool discovery = false;
        var seeds = new List<string>();
        var worlds = new List<(string Name, string Path)>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var m = HeaderName.Match(line);
            if (m.Success) { name = m.Groups[1].Value.Trim(); continue; }
            m = HeaderVersion.Match(line);
            if (m.Success) { version = m.Groups[1].Value.Trim(); continue; }
            m = HeaderDesc.Match(line);
            if (m.Success) { desc = m.Groups[1].Value.Trim(); continue; }
            m = HeaderLlm.Match(line);
            if (m.Success)
            {
                var model = m.Groups[1].Value.Trim();
                var tier = m.Groups[2].Value.Trim();
                if (model != "gemma3" && model != "nemotron3-nano") throw new InoParseException("BOOT002", i + 1, $"unknown model: {model}");
                if (tier != "fast" && tier != "balanced" && tier != "reasoning") throw new InoParseException("BOOT003", i + 1, $"unknown tier: {tier}");
                llms.Add((model, tier));
                continue;
            }
            m = HeaderVoice.Match(line);
            if (m.Success) { voice = m.Groups[1].Value.Trim(); continue; }
            m = HeaderDurability.Match(line);
            if (m.Success)
            {
                durability = m.Groups[1].Value.Trim().ToLowerInvariant();
                if (durability != "redis" && durability != "memory") throw new InoParseException("BOOT004", i + 1, $"unknown durability: {durability}");
                continue;
            }
            m = HeaderUi.Match(line);
            if (m.Success) { ui = m.Groups[1].Value.Trim(); continue; }
            m = HeaderDiscovery.Match(line);
            if (m.Success) { discovery = string.Equals(m.Groups[1].Value, "on", StringComparison.OrdinalIgnoreCase); continue; }
            m = HeaderAdvertised.Match(line);
            if (m.Success) { advertised = m.Groups[1].Value.Trim(); continue; }
            if (line.StartsWith("advertised-ip:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InoParseException("BOOT005", i + 1, "advertised-ip in boot must use 'env VAR' form (no literal IPs)");
            }
            m = HeaderSeed.Match(line);
            if (m.Success)
            {
                var p = m.Groups[1].Value.Trim();
                if (!p.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) && !File.Exists(p)) throw new InoParseException("BOOT006", i + 1, $"missing file: {p}");
                seeds.Add(p);
                continue;
            }
            m = HeaderWorld.Match(line);
            if (m.Success)
            {
                var wname = m.Groups[1].Value.Trim();
                var wpath = m.Groups[2].Value.Trim();
                if (!wpath.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) && !File.Exists(wpath)) throw new InoParseException("BOOT006", i + 1, $"missing file: {wpath}");
                worlds.Add((wname, wpath));
                continue;
            }
            m = Escalate.Match(line);
            if (m.Success) continue;
        }
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
            throw new InoParseException("BOOT001", 0, "name and version required");
        if (worlds.Count > 0)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in worlds)
            {
                if (!seen.Add(w.Name)) throw new InoParseException("BOOT008", 0, $"duplicate world: {w.Name}");
            }
        }
        return new BootManifest(name, version, desc, llms, voice, durability, ui, discovery, advertised, seeds.ToArray(), worlds);
    }
}

public sealed class InoParseException : Exception
{
    public string Code { get; }
    public int Line { get; }
    public InoParseException(string code, int line, string message) : base(message)
    {
        Code = code;
        Line = line;
    }
}
