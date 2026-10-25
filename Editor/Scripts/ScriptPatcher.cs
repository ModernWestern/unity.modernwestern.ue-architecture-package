using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// What a <see cref="ScriptPatcher"/> edit gives back: the new source, or why it refused, and the declaration before
    /// and after for the preview.
    /// </summary>
    internal sealed class PatchResult
    {
        /// <summary>
        /// Why the edit was refused, in plain words; empty when it was not.
        /// </summary>
        public string Problem = string.Empty;

        /// <summary>
        /// The whole file after the edit (the original when nothing changed or the edit was refused).
        /// </summary>
        public string Source = string.Empty;

        /// <summary>
        /// False when the file already was as asked (nothing to write).
        /// </summary>
        public bool Changed;

        /// <summary>
        /// The declaration's line(s) before and after, e.g. "public class GameplayScene : MonoBehaviour".
        /// </summary>
        public string Before = string.Empty;

        public string After = string.Empty;

        /// <summary>
        /// The 1-based line of the declaration.
        /// </summary>
        public int Line;

        public bool Ok => string.IsNullOrEmpty(Problem);

        public static PatchResult Refuse(string source, string problem)
        {
            return new PatchResult { Source = source ?? string.Empty, Problem = problem };
        }
    }

    /// <summary>
    /// Pure, previewable edits of one class declaration in a C# file: change its base class (<see cref="Reparent"/>),
    /// change the GameState and PlayerState of a GameMode&lt;A, B&gt; (<see cref="RetargetGameModeStates"/>), rename the
    /// class inside its file (<see cref="RenameClass"/>) and add a using directive (<see cref="EnsureUsing"/>). Comments,
    /// strings and #if lines are never touched. Anything unusual (a partial, nested, generic or repeated declaration, a
    /// declaration with comments or #if lines inside it) is refused with the reason, so the user changes it by hand.
    /// </summary>
    internal static class ScriptPatcher
    {
        private static readonly Regex Declaration = new(@"(?<![\w@.])(class|struct|interface|enum|record)\s+(@?[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "static", "sealed", "abstract", "partial", "unsafe", "new", "readonly", "ref", "file"
        };

        /// <summary>
        /// One type declaration found in a file (positions are indexes in the source).
        /// </summary>
        internal sealed class TypeDeclaration
        {
            public string Kind;

            public string Name;

            public int HeaderStart;

            public int KeywordIndex;

            public int NameEnd;

            public int BodyOpen = -1;

            public int BodyClose = -1;

            public readonly List<string> Modifiers = new();

            public bool IsNested;
        }

        /// <summary>
        /// Changes the base class of <paramref name="className"/> to <paramref name="newBase"/> (the text to write, e.g.
        /// "GameMode&lt;ArenaGameState, ArenaPlayerState&gt;"). <paramref name="currentBase"/> is the class it derives
        /// from now ("MonoBehaviour"), or null when it derives from nothing: the base list must start with it (interfaces
        /// after it stay). A class that already derives from a class named like <paramref name="newBase"/> is left as it is.
        /// </summary>
        public static PatchResult Reparent(string source, string className, string newBase, string currentBase)
        {
            source ??= string.Empty;

            if (string.IsNullOrWhiteSpace(newBase))
            {
                return PatchResult.Refuse(source, "No base class to write.");
            }

            var masked = Mask(source);

            if (!TryFind_Internal(source, masked, className, out var declaration, out var problem))
            {
                return PatchResult.Refuse(source, problem);
            }

            var entries = BaseEntries_Internal(source, masked, declaration);

            var wanted = SimpleName(newBase);

            if (entries.Count > 0 && SimpleName(entries[0].Text) == wanted)
            {
                return Unchanged_Internal(source, declaration);
            }

            string patched;

            if (entries.Count == 0)
            {
                if (!string.IsNullOrEmpty(currentBase) && SimpleName(currentBase) != "Object")
                {
                    return PatchResult.Refuse(source, $"{className} names no base class in its code, but it derives from {currentBase}. Change it by hand.");
                }

                patched = source.Substring(0, declaration.NameEnd) + " : " + newBase + source.Substring(declaration.NameEnd);
            }
            else if (!string.IsNullOrEmpty(currentBase) && SimpleName(entries[0].Text) == SimpleName(currentBase))
            {
                patched = source.Substring(0, entries[0].Start) + newBase + source.Substring(entries[0].End);
            }
            else if (string.IsNullOrEmpty(currentBase) && LooksLikeInterface_Internal(entries[0].Text))
            {
                patched = source.Substring(0, entries[0].Start) + newBase + ", " + source.Substring(entries[0].Start);
            }
            else
            {
                return PatchResult.Refuse(source, $"The code says {className} derives from {entries[0].Text.Trim()}, not {currentBase ?? "nothing"}. Change it by hand.");
            }

            return Changed_Internal(source, patched, declaration);
        }

        /// <summary>
        /// Changes the GameState and PlayerState a GameMode class names: ": GameMode&lt;A, B&gt;" (or a plain ": GameMode")
        /// becomes ": GameMode&lt;<paramref name="gameState"/>, <paramref name="playerState"/>&gt;". A class that derives
        /// from another GameMode class of yours is refused: its states come from that class.
        /// </summary>
        public static PatchResult RetargetGameModeStates(string source, string className, string gameState, string playerState)
        {
            source ??= string.Empty;

            if (string.IsNullOrWhiteSpace(gameState) || string.IsNullOrWhiteSpace(playerState))
            {
                return PatchResult.Refuse(source, "Name both the GameState and the PlayerState class.");
            }

            var masked = Mask(source);

            if (!TryFind_Internal(source, masked, className, out var declaration, out var problem))
            {
                return PatchResult.Refuse(source, problem);
            }

            var entries = BaseEntries_Internal(source, masked, declaration);

            if (entries.Count == 0 || SimpleName(entries[0].Text) != "GameMode")
            {
                var what = entries.Count == 0 ? "names no base class" : $"derives from {entries[0].Text.Trim()}";

                return PatchResult.Refuse(source, $"{className} {what} in its code, not from GameMode: its GameState and PlayerState come from that class. Change them there.");
            }

            var entry = entries[0];

            var text = source.Substring(entry.Start, entry.End - entry.Start);

            var open = text.IndexOf('<');

            var prefix = open < 0 ? text.TrimEnd() : text.Substring(0, open).TrimEnd();

            if (open >= 0)
            {
                var arguments = SplitTopLevel_Internal(text.Substring(open + 1, text.LastIndexOf('>') - open - 1));

                if (arguments.Count != 2)
                {
                    return PatchResult.Refuse(source, $"{className} names {arguments.Count} classes in GameMode<...>, not 2. Change it by hand.");
                }

                if (Squash_Internal(arguments[0]) == Squash_Internal(gameState) && Squash_Internal(arguments[1]) == Squash_Internal(playerState))
                {
                    return Unchanged_Internal(source, declaration);
                }
            }

            var replacement = $"{prefix}<{gameState.Trim()}, {playerState.Trim()}>";

            var patched = source.Substring(0, entry.Start) + replacement + source.Substring(entry.End);

            return Changed_Internal(source, patched, declaration);
        }

        /// <summary>
        /// Renames <paramref name="oldName"/> to <paramref name="newName"/> wherever the code of this file names it (the
        /// declaration, GetComponent&lt;X&gt;, nameof(X)...); comments and strings stay. Refused when the file already
        /// uses <paramref name="newName"/> or declares the class more than once, and when the old name is not surely the
        /// class everywhere: after a dot (MyGame.X, or a member called X), inside an interpolated string, as an enum value,
        /// or as the name of a field, property, method, variable or parameter.
        /// </summary>
        public static PatchResult RenameClass(string source, string oldName, string newName)
        {
            source ??= string.Empty;

            if (!IsIdentifier(newName))
            {
                return PatchResult.Refuse(source, $"\"{newName}\" is not a valid class name.");
            }

            var masked = Mask(source);

            if (!TryFind_Internal(source, masked, oldName, out var declaration, out var problem))
            {
                return PatchResult.Refuse(source, problem);
            }

            if (oldName == newName)
            {
                return Unchanged_Internal(source, declaration);
            }

            if (Identifiers_Internal(masked, newName).Any())
            {
                return PatchResult.Refuse(source, $"The file already uses the name {newName}. Pick another name.");
            }

            var unsafeUse = UnsafeRename_Internal(source, masked, oldName);

            if (unsafeUse.Length > 0)
            {
                return PatchResult.Refuse(source, unsafeUse);
            }

            var builder = new StringBuilder(source);

            var found = Identifiers_Internal(masked, oldName).OrderByDescending(index => index).ToList();

            foreach (var index in found)
            {
                builder.Remove(index, oldName.Length).Insert(index, newName);
            }

            var shift = found.Count(index => index < declaration.BodyOpen) * (newName.Length - oldName.Length);

            return Changed_Internal(source, builder.ToString(), declaration, declaration.BodyOpen + shift);
        }

        /// <summary>
        /// Adds "using <paramref name="name"/>;" after the file's last using directive that is not inside #if ... #endif
        /// (else after an #if ... #endif block of usings, else at the top, after any #define) unless the file already has it
        /// outside #if or declares its classes inside that namespace. A using inside "#if UNITY_EDITOR" would be missing
        /// from player builds, so it is never the anchor and never counts as already there.
        /// </summary>
        public static PatchResult EnsureUsing(string source, string name)
        {
            source ??= string.Empty;

            var directives = new List<int>();

            var masked = Mask_Internal(source, false, directives);

            var escaped = Regex.Escape(name);

            if (Regex.Matches(masked, @"(?<![\w@.])namespace\s+([\w.]+)").Cast<Match>()
                     .Any(match => match.Groups[1].Value == name || match.Groups[1].Value.StartsWith(name + ".", StringComparison.Ordinal)))
            {
                return new PatchResult { Source = source };
            }

            var firstCode = Regex.Match(masked, @"(?<![\w@.])(namespace|class|struct|interface|enum|record)\b|\[");

            var limit = firstCode.Success ? firstCode.Index : masked.Length;

            // The #if depth after each directive before the first declaration: 0 is code every build compiles.
            var depth = 0;

            var depthAfter = new List<KeyValuePair<int, int>>();

            foreach (var index in directives)
            {
                if (index >= limit)
                {
                    break;
                }

                var keyword = DirectiveKeyword_Internal(source, index);

                if (keyword == "if")
                {
                    depth++;
                }
                else if (keyword == "endif")
                {
                    depth--;
                }

                if (depth < 0)
                {
                    return PatchResult.Refuse(source, $"The #if and #endif lines at the top of the file do not match. Add \"using {name};\" by hand.");
                }

                depthAfter.Add(new KeyValuePair<int, int>(index, depth));
            }

            int DepthAt(int position)
            {
                var found = 0;

                foreach (var pair in depthAfter)
                {
                    if (pair.Key >= position)
                    {
                        break;
                    }

                    found = pair.Value;
                }

                return found;
            }

            // [ \t]* rather than \s*: a match must start on the using's own line, not on a blanked #if line above it.
            if (Regex.Matches(masked, $@"(?m)^[ \t]*using\s+{escaped}\s*;").Cast<Match>().Any(match => DepthAt(match.Index) == 0))
            {
                return new PatchResult { Source = source };
            }

            var newline = source.Contains("\r\n") ? "\r\n" : "\n";

            var line = "using " + name + ";";

            var last = Regex.Matches(masked, @"(?m)^[ \t]*using\s+[^;=(]+;[ \t]*(\r?\n|$)").Cast<Match>().Where(match => match.Index < limit && DepthAt(match.Index) == 0).LastOrDefault();

            if (last != null)
            {
                var insert = last.Index + last.Length;

                var patched = source.Substring(0, insert) + (last.Value.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : newline) + line + newline + source.Substring(insert);

                return new PatchResult { Source = patched, Changed = true, After = line, Line = LineOf_Internal(source, last.Index) + 1 };
            }

            // No using outside #if: after the last #if ... #endif block (or #define) before the first declaration.
            var anchor = -1;

            foreach (var pair in depthAfter)
            {
                var keyword = DirectiveKeyword_Internal(source, pair.Key);

                if (pair.Value == 0 && (keyword == "endif" || keyword == "define" || keyword == "undef"))
                {
                    anchor = pair.Key;
                }
            }

            if (anchor < 0)
            {
                return new PatchResult { Source = line + newline + newline + source, Changed = true, After = line, Line = 1 };
            }

            var lineEnd = source.IndexOf('\n', anchor);

            var after = lineEnd < 0 ? source + newline + line + newline : source.Substring(0, lineEnd + 1) + line + newline + source.Substring(lineEnd + 1);

            return new PatchResult { Source = after, Changed = true, After = line, Line = LineOf_Internal(source, anchor) + 2 };
        }

        // "if", "endif", "define"... for the directive whose '#' is at index.
        private static string DirectiveKeyword_Internal(string source, int index)
        {
            var i = index + 1;

            while (i < source.Length && (source[i] == ' ' || source[i] == '\t'))
            {
                i++;
            }

            var start = i;

            while (i < source.Length && char.IsLetter(source[i]))
            {
                i++;
            }

            return source.Substring(start, i - start);
        }

        /// <summary>
        /// The type declarations of a file, with whether each sits inside another type.
        /// </summary>
        internal static List<TypeDeclaration> Declarations(string source)
        {
            return Declarations_Internal(Mask(source ?? string.Empty));
        }

        /// <summary>
        /// "GameMode" for "global::ModernWestern.UEArchitecture.GameMode&lt;A, B&gt;".
        /// </summary>
        public static string SimpleName(string typeText)
        {
            var text = (typeText ?? string.Empty).Trim();

            var open = text.IndexOf('<');

            if (open >= 0)
            {
                text = text.Substring(0, open);
            }

            var colons = text.LastIndexOf("::", StringComparison.Ordinal);

            if (colons >= 0)
            {
                text = text.Substring(colons + 2);
            }

            var dot = text.LastIndexOf('.');

            return (dot >= 0 ? text.Substring(dot + 1) : text).Trim().TrimStart('@');
        }

        public static bool IsIdentifier(string name)
        {
            return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") && !CSharpKeywords.Contains(name);
        }

        /// <summary>
        /// The source with every comment, string, character literal and preprocessor line replaced by spaces (line breaks
        /// kept), so positions stay the same and only code is searched. With <paramref name="keepHoles"/>, the code inside
        /// the holes of an interpolated string ($"{nameof(Hero)}") is kept; only its text parts are blanked.
        /// </summary>
        public static string Mask(string source, bool keepHoles = false)
        {
            return Mask_Internal(source, keepHoles, null);
        }

        // Mask, adding the index of each preprocessor line's '#' to directives (when not null).
        private static string Mask_Internal(string source, bool keepHoles, List<int> directives)
        {
            var text = (source ?? string.Empty).ToCharArray();

            var length = text.Length;

            var lineHasCode = false;

            var i = 0;

            while (i < length)
            {
                var c = text[i];

                var next = i + 1 < length ? text[i + 1] : '\0';

                if (c == '\n')
                {
                    lineHasCode = false;

                    i++;

                    continue;
                }

                if (c == '#' && !lineHasCode)
                {
                    directives?.Add(i);

                    i = Blank_Internal(text, i, EndOfLine_Internal(text, i));

                    continue;
                }

                if (!char.IsWhiteSpace(c))
                {
                    lineHasCode = true;
                }

                if (c == '/' && next == '/')
                {
                    i = Blank_Internal(text, i, EndOfLine_Internal(text, i));

                    continue;
                }

                if (c == '/' && next == '*')
                {
                    var end = IndexOf_Internal(text, "*/", i + 2);

                    i = Blank_Internal(text, i, end < 0 ? length : end + 2);

                    continue;
                }

                if (c == '\'')
                {
                    var end = i + 1;

                    while (end < length && text[end] != '\'' && text[end] != '\n')
                    {
                        end += text[end] == '\\' ? 2 : 1;
                    }

                    i = Blank_Internal(text, i, Math.Min(length, end + 1));

                    continue;
                }

                if (keepHoles && StartsString_Internal(text, i))
                {
                    i = MaskKeepingHoles_Internal(text, i);

                    continue;
                }

                if (c == '"' || (c == '@' || c == '$') && (next == '"' || (next == '@' || next == '$') && i + 2 < length && text[i + 2] == '"'))
                {
                    var start = i;

                    var verbatim = false;

                    var interpolated = false;

                    while (text[i] != '"')
                    {
                        verbatim |= text[i] == '@';

                        interpolated |= text[i] == '$';

                        i++;
                    }

                    i = Blank_Internal(text, start, StringEnd_Internal(text, i + 1, verbatim, interpolated));

                    continue;
                }

                i++;
            }

            return new string(text);
        }

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        // The end (exclusive) of a string literal whose opening quote is just before start.
        private static int StringEnd_Internal(char[] text, int start, bool verbatim, bool interpolated)
        {
            var length = text.Length;

            var depth = 0;

            var i = start;

            while (i < length)
            {
                var c = text[i];

                if (interpolated && depth == 0 && c == '{')
                {
                    if (i + 1 < length && text[i + 1] == '{')
                    {
                        i += 2;

                        continue;
                    }

                    depth++;

                    i++;

                    continue;
                }

                if (depth > 0)
                {
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                    }
                    else if (c == '"')
                    {
                        // A plain string inside a hole of an interpolated string.
                        i = StringEnd_Internal(text, i + 1, false, false);

                        continue;
                    }

                    i++;

                    continue;
                }

                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < length && text[i + 1] == '"')
                        {
                            i += 2;

                            continue;
                        }

                        return i + 1;
                    }

                    i++;

                    continue;
                }

                if (c == '\\')
                {
                    i += 2;

                    continue;
                }

                if (c == '"' || c == '\n')
                {
                    return i + 1;
                }

                i++;
            }

            return length;
        }

        // True when a string literal starts at i: "...", @"...", $"...", $@"..." or @$"...".
        private static bool StartsString_Internal(char[] text, int i)
        {
            var length = text.Length;

            var c = text[i];

            var next = i + 1 < length ? text[i + 1] : '\0';

            return c == '"' || (c == '@' || c == '$') && (next == '"' || (next == '@' || next == '$') && i + 2 < length && text[i + 2] == '"');
        }

        // Blanks the string literal that starts at i (its $ or @ prefix, or its quote), keeping the code in the holes of an
        // interpolated string; strings inside those holes are blanked the same way. Returns its end (exclusive).
        private static int MaskKeepingHoles_Internal(char[] text, int i)
        {
            var length = text.Length;

            var start = i;

            var verbatim = false;

            var interpolated = false;

            while (i < length && text[i] != '"')
            {
                verbatim |= text[i] == '@';

                interpolated |= text[i] == '$';

                i++;
            }

            if (!interpolated)
            {
                return Blank_Internal(text, start, StringEnd_Internal(text, i + 1, verbatim, false));
            }

            i = Blank_Internal(text, start, i + 1);

            while (i < length)
            {
                var c = text[i];

                if (c == '{' && i + 1 < length && text[i + 1] == '{')
                {
                    i = Blank_Internal(text, i, i + 2);

                    continue;
                }

                if (c == '{')
                {
                    text[i] = ' ';

                    i++;

                    // A hole: code up to its closing brace.
                    var depth = 1;

                    while (i < length && depth > 0)
                    {
                        c = text[i];

                        if (StartsString_Internal(text, i))
                        {
                            i = MaskKeepingHoles_Internal(text, i);

                            continue;
                        }

                        if (c == '\'')
                        {
                            var end = i + 1;

                            while (end < length && text[end] != '\'' && text[end] != '\n')
                            {
                                end += text[end] == '\\' ? 2 : 1;
                            }

                            i = Blank_Internal(text, i, Math.Min(length, end + 1));

                            continue;
                        }

                        if (c == '{')
                        {
                            depth++;
                        }
                        else if (c == '}' && --depth == 0)
                        {
                            text[i] = ' ';
                        }

                        i++;
                    }

                    continue;
                }

                if (verbatim)
                {
                    if (c == '"' && i + 1 < length && text[i + 1] == '"')
                    {
                        i = Blank_Internal(text, i, i + 2);

                        continue;
                    }

                    if (c == '"')
                    {
                        return Blank_Internal(text, i, i + 1);
                    }
                }
                else
                {
                    if (c == '\\')
                    {
                        i = Blank_Internal(text, i, Math.Min(length, i + 2));

                        continue;
                    }

                    if (c == '"')
                    {
                        return Blank_Internal(text, i, i + 1);
                    }

                    if (c == '\n')
                    {
                        return i + 1;
                    }
                }

                i = Blank_Internal(text, i, i + 1);
            }

            return length;
        }

        private static int Blank_Internal(char[] text, int start, int end)
        {
            end = Math.Min(end, text.Length);

            for (var i = start; i < end; i++)
            {
                if (text[i] != '\n' && text[i] != '\r')
                {
                    text[i] = ' ';
                }
            }

            return end;
        }

        private static int EndOfLine_Internal(char[] text, int from)
        {
            var i = from;

            while (i < text.Length && text[i] != '\n')
            {
                i++;
            }

            return i;
        }

        private static int IndexOf_Internal(char[] text, string value, int from)
        {
            for (var i = from; i <= text.Length - value.Length; i++)
            {
                var match = true;

                for (var j = 0; j < value.Length && match; j++)
                {
                    match = text[i + j] == value[j];
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<TypeDeclaration> Declarations_Internal(string masked)
        {
            var found = new List<TypeDeclaration>();

            foreach (Match match in Declaration.Matches(masked))
            {
                var name = match.Groups[2].Value.TrimStart('@');

                // "where T : class where U : ..." names no type.
                if (name == "where" || CSharpKeywords.Contains(name))
                {
                    continue;
                }

                var declaration = new TypeDeclaration
                {
                    Kind = match.Groups[1].Value,
                    Name = name,
                    KeywordIndex = match.Index,
                    NameEnd = match.Index + match.Length,
                    HeaderStart = match.Index
                };

                // The modifiers before the keyword ("public sealed partial").
                var cursor = match.Index;

                while (true)
                {
                    var end = cursor;

                    while (end > 0 && char.IsWhiteSpace(masked[end - 1]))
                    {
                        end--;
                    }

                    var start = end;

                    while (start > 0 && (char.IsLetterOrDigit(masked[start - 1]) || masked[start - 1] == '_'))
                    {
                        start--;
                    }

                    var word = masked.Substring(start, end - start);

                    if (word.Length == 0 || !Modifiers.Contains(word))
                    {
                        break;
                    }

                    declaration.Modifiers.Insert(0, word);

                    declaration.HeaderStart = start;

                    cursor = start;
                }

                var parens = 0;

                for (var i = declaration.NameEnd; i < masked.Length; i++)
                {
                    var c = masked[i];

                    if (c == '(')
                    {
                        parens++;
                    }
                    else if (c == ')')
                    {
                        parens--;
                    }
                    else if (parens == 0 && c == ';')
                    {
                        break;
                    }
                    else if (parens == 0 && c == '{')
                    {
                        declaration.BodyOpen = i;

                        break;
                    }
                }

                if (declaration.BodyOpen >= 0)
                {
                    var depth = 0;

                    for (var i = declaration.BodyOpen; i < masked.Length; i++)
                    {
                        if (masked[i] == '{')
                        {
                            depth++;
                        }
                        else if (masked[i] == '}' && --depth == 0)
                        {
                            declaration.BodyClose = i;

                            break;
                        }
                    }
                }

                found.Add(declaration);
            }

            foreach (var declaration in found)
            {
                declaration.IsNested = found.Any(other => other != declaration && other.BodyOpen >= 0 && other.BodyClose > other.BodyOpen &&
                                                          declaration.KeywordIndex > other.BodyOpen && declaration.KeywordIndex < other.BodyClose);
            }

            return found;
        }

        private static bool TryFind_Internal(string source, string masked, string className, out TypeDeclaration declaration, out string problem)
        {
            declaration = null;

            problem = string.Empty;

            var matches = Declarations_Internal(masked).Where(found => found.Name == className).ToList();

            if (matches.Count == 0)
            {
                problem = $"The file declares no class called {className}.";

                return false;
            }

            if (matches.Count > 1)
            {
                problem = $"{className} is declared {matches.Count} times in this file (#if branches or several parts). Change it by hand.";

                return false;
            }

            declaration = matches[0];

            if (declaration.Kind != "class")
            {
                problem = $"{className} is a {declaration.Kind}, not a class.";

                return false;
            }

            if (declaration.IsNested)
            {
                problem = $"{className} is declared inside another class. Move it to its own file first.";

                return false;
            }

            if (declaration.Modifiers.Contains("partial"))
            {
                problem = $"{className} is partial: its other parts may name a base class too. Change it by hand.";

                return false;
            }

            if (declaration.Modifiers.Contains("static"))
            {
                problem = $"{className} is static, so it cannot be a component.";

                return false;
            }

            if (declaration.BodyOpen < 0 || declaration.BodyClose < 0)
            {
                problem = $"Could not find the body of {className} ({{ ... }}). Change it by hand.";

                return false;
            }

            var after = declaration.NameEnd;

            while (after < masked.Length && char.IsWhiteSpace(masked[after]))
            {
                after++;
            }

            if (after < masked.Length && masked[after] == '<')
            {
                problem = $"{className} is generic, so Unity cannot add it as a component. Change it by hand.";

                return false;
            }

            var header = source.Substring(declaration.HeaderStart, declaration.BodyOpen - declaration.HeaderStart);

            if (header != masked.Substring(declaration.HeaderStart, declaration.BodyOpen - declaration.HeaderStart))
            {
                problem = $"The declaration of {className} has comments or #if lines inside it. Change it by hand.";

                return false;
            }

            return true;
        }

        private readonly struct BaseEntry
        {
            public readonly int Start;

            public readonly int End;

            public readonly string Text;

            public BaseEntry(int start, int end, string text)
            {
                Start = start;

                End = end;

                Text = text;
            }
        }

        // The base list (": A, IB") of a declaration, entries trimmed.
        private static List<BaseEntry> BaseEntries_Internal(string source, string masked, TypeDeclaration declaration)
        {
            var entries = new List<BaseEntry>();

            var listStart = -1;

            var listEnd = declaration.BodyOpen;

            var where = Regex.Match(masked.Substring(declaration.NameEnd, declaration.BodyOpen - declaration.NameEnd), @"(?<![\w@.])where\b");

            if (where.Success)
            {
                listEnd = declaration.NameEnd + where.Index;
            }

            var angle = 0;

            for (var i = declaration.NameEnd; i < listEnd; i++)
            {
                if (masked[i] == '<')
                {
                    angle++;
                }
                else if (masked[i] == '>')
                {
                    angle--;
                }
                else if (masked[i] == ':' && angle == 0 && !(i + 1 < listEnd && masked[i + 1] == ':') && !(i > 0 && masked[i - 1] == ':'))
                {
                    listStart = i;

                    break;
                }
            }

            if (listStart < 0)
            {
                return entries;
            }

            var start = listStart + 1;

            angle = 0;

            for (var i = listStart + 1; i <= listEnd; i++)
            {
                var c = i < listEnd ? masked[i] : ',';

                if (c == '<' || c == '(')
                {
                    angle++;
                }
                else if (c == '>' || c == ')')
                {
                    angle--;
                }
                else if (c == ',' && angle == 0)
                {
                    var from = start;

                    var to = i;

                    while (from < to && char.IsWhiteSpace(source[from]))
                    {
                        from++;
                    }

                    while (to > from && char.IsWhiteSpace(source[to - 1]))
                    {
                        to--;
                    }

                    if (to > from)
                    {
                        entries.Add(new BaseEntry(from, to, source.Substring(from, to - from)));
                    }

                    start = i + 1;
                }
            }

            return entries;
        }

        private static List<string> SplitTopLevel_Internal(string text)
        {
            var parts = new List<string>();

            var depth = 0;

            var start = 0;

            for (var i = 0; i <= text.Length; i++)
            {
                var c = i < text.Length ? text[i] : ',';

                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start).Trim());

                    start = i + 1;
                }
            }

            return parts;
        }

        private static IEnumerable<int> Identifiers_Internal(string masked, string name)
        {
            foreach (Match match in Regex.Matches(masked, $@"(?<![\w.])@?{Regex.Escape(name)}(?![\w])"))
            {
                yield return masked[match.Index] == '@' ? match.Index + 1 : match.Index;
            }
        }

        // Keywords that name a type: "int Hero;" declares something called Hero.
        private static readonly HashSet<string> TypeKeywords = new(StringComparer.Ordinal)
        {
            "bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "sbyte", "short", "string", "uint", "ulong", "ushort", "void"
        };

        // Contextual keywords that come before an expression, not a type: "is not Hero", "await Hero.Load()".
        private static readonly HashSet<string> ExpressionWords = new(StringComparer.Ordinal)
        {
            "not", "and", "or", "await", "when", "where", "select", "from", "orderby", "group", "into", "let", "join", "on", "equals", "by", "ascending", "descending",
            "yield", "nameof", "with", "init", "get", "set", "add", "remove", "value", "async"
        };

        // Why renaming every code use of oldName in this file may change something that is not the class, or empty.
        private static string UnsafeRename_Internal(string source, string masked, string oldName)
        {
            var escaped = Regex.Escape(oldName);

            var holes = Mask_Internal(source, true, null);

            var dotted = Regex.Match(holes, $@"(?<=\.\s*)@?{escaped}(?!\w)");

            if (dotted.Success)
            {
                return $"{oldName} also comes after a dot in this file (line {LineOf_Internal(source, dotted.Index) + 1}: a qualified name like MyGame.{oldName}, or a member called {oldName}). " +
                       "Rename it in your code editor, or convert without renaming.";
            }

            var inCode = Identifiers_Internal(masked, oldName).ToList();

            var inHoles = Identifiers_Internal(holes, oldName).Where(index => !inCode.Contains(index)).ToList();

            if (inHoles.Count > 0)
            {
                return $"{oldName} is named inside an interpolated string in this file (line {LineOf_Internal(source, inHoles[0]) + 1}). Rename it in your code editor, or convert without renaming.";
            }

            var enums = Declarations_Internal(masked).Where(declaration => declaration.Kind == "enum" && declaration.BodyOpen >= 0 && declaration.BodyClose > declaration.BodyOpen).ToList();

            foreach (var index in inCode)
            {
                if (enums.Any(declaration => index > declaration.BodyOpen && index < declaration.BodyClose) || IsDeclaredName_Internal(masked, index, oldName.Length))
                {
                    return $"{oldName} is also the name of a member, enum value, variable or parameter in this file (line {LineOf_Internal(source, index) + 1}): renaming the class " +
                           "would rename it too, and a renamed field loses its saved values. Rename that one first, or convert without renaming.";
                }
            }

            return string.Empty;
        }

        // True when the identifier at index is the name being declared ("Hero Hero;", "int Hero(", "var Hero ="), not a use
        // of a type.
        private static bool IsDeclaredName_Internal(string masked, int index, int length)
        {
            var after = index + length;

            while (after < masked.Length && char.IsWhiteSpace(masked[after]))
            {
                after++;
            }

            var next = after < masked.Length ? masked[after] : '\0';

            if (";=({,)".IndexOf(next) < 0 || next == '=' && after + 1 < masked.Length && masked[after + 1] == '=')
            {
                return false;
            }

            var before = index;

            while (before > 0 && char.IsWhiteSpace(masked[before - 1]))
            {
                before--;
            }

            if (before == 0)
            {
                return false;
            }

            var previous = masked[before - 1];

            var beforePrevious = before > 1 ? masked[before - 2] : '\0';

            switch (previous)
            {
                // "List<Hero> Hero", "Hero[] Hero", "Hero? Hero"; not "=> Hero", "-> Hero" or "?? Hero".
                case '>':
                    return beforePrevious != '=' && beforePrevious != '-';

                case ']':
                    return true;

                case '?':
                    return beforePrevious != '?';
            }

            if (!char.IsLetterOrDigit(previous) && previous != '_')
            {
                return false;
            }

            var start = before - 1;

            while (start > 0 && (char.IsLetterOrDigit(masked[start - 1]) || masked[start - 1] == '_'))
            {
                start--;
            }

            var word = masked.Substring(start, before - start);

            if (TypeKeywords.Contains(word))
            {
                return true;
            }

            return !CSharpKeywords.Contains(word) && !ExpressionWords.Contains(word) && !char.IsDigit(word[0]);
        }

        private static bool LooksLikeInterface_Internal(string typeText)
        {
            var name = SimpleName(typeText);

            return name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);
        }

        private static string Squash_Internal(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        }

        private static PatchResult Unchanged_Internal(string source, TypeDeclaration declaration)
        {
            var header = HeaderText_Internal(source, declaration.HeaderStart, declaration.BodyOpen);

            return new PatchResult { Source = source, Before = header, After = header, Line = LineOf_Internal(source, declaration.KeywordIndex) + 1 };
        }

        private static PatchResult Changed_Internal(string source, string patched, TypeDeclaration declaration, int newBodyOpen = -1)
        {
            var delta = newBodyOpen >= 0 ? newBodyOpen - declaration.BodyOpen : patched.Length - source.Length;

            var lineStart = LineStart_Internal(source, declaration.HeaderStart);

            return new PatchResult
            {
                Source = patched,
                Changed = true,
                Before = HeaderText_Internal(source, lineStart, declaration.BodyOpen),
                After = HeaderText_Internal(patched, lineStart, declaration.BodyOpen + delta),
                Line = LineOf_Internal(source, declaration.KeywordIndex) + 1
            };
        }

        private static string HeaderText_Internal(string source, int from, int to)
        {
            from = LineStart_Internal(source, from);

            return to > from ? source.Substring(from, to - from).Trim() : string.Empty;
        }

        private static int LineStart_Internal(string source, int index)
        {
            while (index > 0 && source[index - 1] != '\n')
            {
                index--;
            }

            return index;
        }

        private static int LineOf_Internal(string source, int index)
        {
            var line = 0;

            for (var i = 0; i < index && i < source.Length; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }
}
