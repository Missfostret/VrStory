using System;
using System.Collections.Generic;

namespace Dialogue.Scripts
{
    /// <summary>
    /// Parser for a lightweight dialogue scripting format that produces a set of named <see cref="MiniNode"/> objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The input text is interpreted as a sequence of nodes, each with a <c>title:</c> header and a body.
    /// Nodes are stored in a dictionary keyed by node name (case-insensitive).
    /// </para>
    ///
    /// <para><b>Node structure</b></para>
    /// <code>
    /// title: Intro
    /// ---
    /// Hello there!
    /// -> Option A [if hasKey]
    ///     &lt;&lt;jump NextNode&gt;&gt;
    /// -> Option B
    ///     &lt;&lt;set hasKey = true&gt;&gt;
    /// &lt;&lt;if hasKey&gt;&gt;
    ///     &lt;&lt;jump HasKeyNode&gt;&gt;
    /// &lt;&lt;else&gt;&gt;
    ///     &lt;&lt;jump NoKeyNode&gt;&gt;
    /// &lt;&lt;endif&gt;&gt;
    /// ===
    /// </code>
    ///
    /// <para><b>Body rules</b></para>
    /// <list type="bullet">
    /// <item><description>Plain lines become <see cref="LineStep"/>.</description></item>
    /// <item><description>Lines starting with <c>-></c> begin a choice block and become a <see cref="ChoiceStep"/>.</description></item>
    /// <item><description>Command lines are wrapped in <c>&lt;&lt; ... &gt;&gt;</c> and become <see cref="CommandStep"/>.</description></item>
    /// <item><description><c>&lt;&lt;if ...&gt;&gt;</c> begins a multi-line conditional block ending at <c>&lt;&lt;endif&gt;&gt;</c>.</description></item>
    /// </list>
    /// </remarks>
    public static class MiniDlgParser
    {
        /// <summary>
        /// Parses the dialogue script text into nodes keyed by node name.
        /// </summary>
        /// <param name="text">Raw dialogue script contents.</param>
        /// <returns>
        /// Dictionary of parsed nodes, keyed by node name (case-insensitive).
        /// </returns>
        /// <exception cref="Exception">
        /// Thrown when the script contains invalid structure or malformed commands.
        /// </exception>
        public static Dictionary<string, MiniNode> ParseNodesAndLines(string text)
        {
            var nodes = new Dictionary<string, MiniNode>(StringComparer.OrdinalIgnoreCase);

            // Normalize line endings to '\n' then split.
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            MiniNode current = null;
            var inBody = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var trimmed = raw.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // Node header: "title: NodeName"
                if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = trimmed.Substring("title:".Length).Trim();
                    current = new MiniNode { Name = name };
                    nodes[name] = current;
                    inBody = false;
                    continue;
                }

                // Body delimiter: "---"
                if (trimmed == "---")
                {
                    if (current == null)
                        throw new Exception("Found '---' before any 'title:'");
                    inBody = true;
                    continue;
                }

                // Node end: "==="
                if (trimmed == "===")
                {
                    current = null;
                    inBody = false;
                    continue;
                }

                // Ignore anything outside a node body.
                if (!inBody || current == null)
                    continue;

                // Choice block: lines starting with "->"
                if (trimmed.StartsWith("->"))
                {
                    var choiceStep = ParseChoices(lines, ref i);
                    current.Steps.Add(choiceStep);
                    continue;
                }

                // Standalone command line: "<< ... >>"
                if (IsCommandLine(trimmed))
                {
                    var inner = UnwrapCommand(trimmed);

                    // Multi-line if-block: "<<if ...>>" ... "<<endif>>"
                    if (inner.StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
                        inner.Equals("if", StringComparison.OrdinalIgnoreCase))
                    {
                        // ParseIfStepBlock consumes until <<endif>> and returns an IfStep
                        // that may contain lines, commands, and choices.
                        var ifStep = ParseIfStepBlock(lines, ref i);
                        current.Steps.Add(ifStep);

                        // ParseIfBlock returns with i positioned at the <<endif>> line.
                        // The for-loop will i++ next, so that's fine.
                        continue;
                    }

                    // Single-line command.
                    current.Steps.Add(new CommandStep(ParseCommand(inner)));
                    continue;
                }

                // Any other body line is treated as a dialogue line.
                current.Steps.Add(new LineStep(raw.TrimEnd()));
            }

            return nodes;
        }

        /// <summary>
        /// Parses a contiguous block of choice lines beginning at the current index.
        /// </summary>
        /// <param name="lines">All lines in the script.</param>
        /// <param name="i">
        /// Current line index. On return, the index is adjusted so the caller continues at the first non-choice line.
        /// </param>
        /// <returns>A <see cref="ChoiceStep"/> containing the parsed choices and their commands.</returns>
        /// <exception cref="Exception">Thrown if a choice contains a malformed command line.</exception>
        private static ChoiceStep ParseChoices(string[] lines, ref int i)
        {
            var step = new ChoiceStep();

            while (i < lines.Length)
            {
                var raw = lines[i];
                var trimmed = raw.Trim();

                if (!trimmed.StartsWith("->"))
                    break;

                // Everything after "->" is the choice text, optionally with a condition.
                var choiceRaw = trimmed[2..].Trim();
                var (choiceText, condVar, timeSec) = ParseChoiceTags(choiceRaw);
                var choice = new Choice(choiceText, condVar);
                if (timeSec.HasValue)
                {
                    if(!step.TimeLimitSeconds.HasValue || timeSec.Value < step.TimeLimitSeconds.Value)
                        step.TimeLimitSeconds = timeSec.Value;
                }
                i++;

                // Parse indented command lines under the choice.
                while (i < lines.Length)
                {
                    var nextRaw = lines[i];

                    // Skip blank lines inside the choice block.
                    if (string.IsNullOrWhiteSpace(nextRaw))
                    {
                        i++;
                        continue;
                    }

                    // Stop if the next choice begins.
                    if (nextRaw.TrimStart().StartsWith("->"))
                        break;

                    // Stop if not indented (choice block ends).
                    if (!IsIndented(nextRaw))
                        break;

                    // Choice commands must be written as "<<command ...>>"
                    var cmd = nextRaw.Trim();
                    if (cmd.StartsWith("<<") && cmd.EndsWith(">>"))
                    {
                        cmd = cmd.Substring(2, cmd.Length - 4).Trim();
                        choice.Commands.Add(ParseCommand(cmd));
                    }
                    else
                    {
                        throw new Exception($"Expected command like <<jump Node>> under choice, got: {nextRaw}");
                    }

                    i++;
                }

                step.Choices.Add(choice);
            }

            // IMPORTANT: Caller expects i to be positioned at the first non-choice line,
            // but the outer for-loop will i++ again; adjust by -1.
            i--;

            return step;
        }

        /// <summary>
        /// Parses a single command string (without the surrounding &lt;&lt; &gt;&gt;).
        /// </summary>
        /// <param name="cmd">Command text (e.g. <c>jump Intro</c> or <c>set hasKey = true</c>).</param>
        /// <returns>An <see cref="ICommand"/> instance.</returns>
        /// <exception cref="Exception">Thrown if the command is empty or unknown.</exception>
        private static ICommand ParseCommand(string cmd)
        {
            // Supports: "jump <NodeName>" and "set <name> = <value>"
            var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) throw new Exception("Empty command");

            var head = parts[0].ToLowerInvariant();
            var tail = parts.Length > 1 ? parts[1].Trim() : "";

            return head switch
            {
                "jump" => new JumpCommand(tail),
                "set" => ParseSet(tail),
                _ => throw new Exception($"Unknown command: {head}")
            };
        }

        /// <summary>
        /// Parses a <c>set</c> command tail in the form <c>name = value</c>.
        /// </summary>
        /// <param name="tail">Everything after <c>set</c>.</param>
        /// <returns>A <see cref="SetCommand"/> instance.</returns>
        private static SetCommand ParseSet(string tail)
        {
            var eq = tail.IndexOf('=');
            if (eq < 0) throw new Exception($"Bad set syntax ( expected name = value): {tail}");

            var name = tail[..eq].Trim();
            var rawValue = tail[(eq + 1)..].Trim();

            var value = ParseValue(rawValue);
            return new SetCommand(name, value);
        }

        /// <summary>
        /// Converts a raw string value into a typed value used by <see cref="SetCommand"/>.
        /// </summary>
        /// <param name="raw">
        /// Raw value text. Supports booleans (<c>true</c>/<c>false</c>),
        /// quoted strings (<c>"hello"</c>), and numbers (parsed as <see cref="double"/> using invariant culture).
        /// </param>
        /// <returns>A boxed value: <see cref="bool"/>, <see cref="double"/>, <see cref="string"/>, or raw string if unknown.</returns>
        private static object ParseValue(string raw)
        {
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

            // Quoted string: "..."
            if (raw.Length >= 2 && raw.StartsWith("\"") && raw.EndsWith("\""))
                return raw.Substring(1, raw.Length - 2);

            // Number (double) using invariant culture.
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;

            // Fallback: treat as a raw string token.
            return raw;
        }

        /// <summary>
        /// Parses a multi-line if-block starting at the current line, returning a <see cref="CommandStep"/>
        /// that wraps an <see cref="IfCommand"/>.
        /// </summary>
        /// <param name="lines">All lines in the script.</param>
        /// <param name="i">
        /// Index pointing at the <c>&lt;&lt;if ...&gt;&gt;</c> line. On return, remains positioned at the <c>&lt;&lt;endif&gt;&gt;</c> line.
        /// </param>
        /// <returns>A <see cref="CommandStep"/> containing the parsed <see cref="IfCommand"/>.</returns>
        /// <exception cref="Exception">Thrown if the block is malformed or reaches EOF without <c>&lt;&lt;endif&gt;&gt;</c>.</exception>
        private static CommandStep ParseIfBlock(string[] lines, ref int i)
        {
            // lines[i] is the line containing "<<if ...>>"
            var header = UnwrapCommand(lines[i].Trim());

            // Header string is expected to start with "if"
            var condition = header[2..].Trim();

            var ifCommand = new IfCommand(condition);

            // Move to next line after "<<if ...>>"
            i++;

            var inElse = false;

            while (i < lines.Length)
            {
                var raw = lines[i];
                var trimmed = raw.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    i++;
                    continue;
                }

                if (!IsCommandLine(trimmed))
                    throw new Exception($"Expected a command line inside if-block, got: {raw}");

                var inner = UnwrapCommand(trimmed);

                if (inner.Equals("else", StringComparison.OrdinalIgnoreCase))
                {
                    inElse = true;
                    i++;
                    continue;
                }

                if (inner.Equals("endif", StringComparison.OrdinalIgnoreCase))
                {
                    // Done. Return with i still pointing at the endif line.
                    return new CommandStep(ifCommand);
                }

                // Normal command inside if/else
                var cmd = ParseCommand(inner);
                if (!inElse) ifCommand.ThenCommands.Add(cmd);
                else ifCommand.ElseCommands.Add(cmd);

                i++;
            }

            throw new Exception("Reached end of file while parsing if-block (missing <<endif>>).");
        }

        private static IfStep ParseIfStepBlock(string[] lines, ref int i)
        {
            var header = UnwrapCommand(lines[i].Trim());
            var condition = header[2..].Trim();
            
            var ifStep = new IfStep(condition);

            i++;

            var inElse = false;

            while (i < lines.Length)
            {
                var raw = lines[i];
                var trimmed = raw.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    i++;
                    continue;
                }

                if (IsCommandLine(trimmed))
                {
                    var inner = UnwrapCommand(trimmed);

                    if (inner.Equals("else", StringComparison.OrdinalIgnoreCase))
                    {
                        inElse = true;
                        i++;
                        continue;
                    }

                    if (inner.Equals("endif", StringComparison.OrdinalIgnoreCase))
                    {
                        return ifStep;
                    }
                    
                    var cmdStep = new CommandStep(ParseCommand(inner));
                    (inElse ? ifStep.ElseSteps : ifStep.ThenSteps).Add(cmdStep);
                    i++;
                    continue;
                }

                if (trimmed.StartsWith("->"))
                {
                    var choiceStep = ParseChoices(lines, ref i);
                    (inElse ? ifStep.ElseSteps : ifStep.ThenSteps).Add(choiceStep);
                    i++;
                    continue;
                }
                
                (inElse ? ifStep.ElseSteps : ifStep.ThenSteps).Add(new LineStep(raw.TrimEnd()));
                i++;
            }

            throw new Exception("Reached end of file hwile parsing if-step block (missing <<endif>>).");
        }

        /// <summary>
        /// Returns true if the given line begins with indentation (4 spaces or a tab).
        /// Used to detect commands belonging to a choice.
        /// </summary>
        private static bool IsIndented(string s) =>
            s.StartsWith("    ") || s.StartsWith("\t");

        /// <summary>
        /// Returns true if the trimmed line is a command line in the form <c>&lt;&lt; ... &gt;&gt;</c>.
        /// </summary>
        private static bool IsCommandLine(string trimmed) =>
            trimmed.StartsWith("<<") && trimmed.EndsWith(">>");

        /// <summary>
        /// Removes the <c>&lt;&lt;</c> and <c>&gt;&gt;</c> wrapper from a command line and trims the inner text.
        /// </summary>
        private static string UnwrapCommand(string trimmed) =>
            trimmed.Substring(2, trimmed.Length - 4).Trim();

        /// <summary>
        /// Parses a choice label that may include an inline condition marker: <c>[if varName]</c>.
        /// </summary>
        /// <param name="choiceRaw">
        /// Raw choice string (everything after <c>-></c>).
        /// Example: <c>Open door [if hasKey]</c>.
        /// </param>
        /// <returns>
        /// Tuple of choice text and condition variable name. If no condition is present, condition is null.
        /// </returns>
        /// <exception cref="Exception">Thrown if the condition bracket is malformed or empty.</exception>
        private static (string text, string condVar) ParseChoiceTextAndCondition(string choiceRaw)
        {
            // Supports: "Open door [if hasKey]" -> ("Open door", "hasKey")
            const string token = "[if ";
            var idx = choiceRaw.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (choiceRaw.Trim(), null);

            var end = choiceRaw.IndexOf(']', idx);
            if (end < 0) throw new Exception("Choice condition missing closing ']' : " + choiceRaw);

            var text = choiceRaw[..idx].Trim();
            var cond = choiceRaw.Substring(idx + token.Length, end - (idx + token.Length)).Trim();

            return string.IsNullOrWhiteSpace(cond)
                ? throw new Exception("Empty [if ...] condition in: " + choiceRaw)
                : (text, cond);
        }

        /// <summary>
        /// Parses the raw text of a choice line and extracts its display text,
        /// optional condition variable, and optional time limit.
        /// </summary>
        /// <param name="choiceRaw">
        /// The raw text following the "->" marker in a dialogue script.
        /// </param>
        /// <returns>
        /// A tuple containing:
        /// <list type="bullet">
        /// <item><description>The cleaned display text</description></item>
        /// <item><description>The optional condition variable</description></item>
        /// <item><description>The optional time limit in seconds</description></item>
        /// </list>
        /// </returns>
        /// <remarks>
        /// Supported tags:
        /// <list type="bullet">
        /// <item><description><c>[if hasKey]</c></description></item>
        /// <item><description><c>[if !hasKey]</c></description></item>
        /// <item><description><c>[time 3]</c></description></item>
        /// </list>
        /// Tags may appear in any order.
        /// </remarks>
        /// <exception cref="Exception">
        /// Thrown if a <c>[time]</c> tag is malformed.
        /// </exception>
        private static (string text, string condVar, float? timeSeconds)
            ParseChoiceTags(string choiceRaw)
        {
            var text = choiceRaw;
            string condVar = null;
            float? time = null;
            
            // Parse [if ...]
            (text, condVar) = ParseChoiceTextAndCondition(text);
            
            // Parse [time N]
            const string token = "[time ";
            var idx = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var end = text.IndexOf(']', idx);
                if (end < 0)
                    throw new Exception("Choice time missing closing ']': " + choiceRaw);

                var before = text[..idx].Trim();
                var inside = text.Substring(
                    idx + token.Length,
                    end - (idx + token.Length)
                ).Trim();

                if (!float.TryParse(
                        inside,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seconds))
                {
                    throw new Exception("Invalid time value: " + inside);
                }

                time = seconds;
                text = before;
            }

            return (text.Trim(), condVar, time);
        }
    }
}
