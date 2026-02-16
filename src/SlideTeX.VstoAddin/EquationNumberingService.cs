// SlideTeX Note: Static equation numbering logic extracted from SlideTeXAddinController.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SlideTeX.VstoAddin.Models;

namespace SlideTeX.VstoAddin
{
    internal static class EquationNumberingService
    {
        private static readonly Regex NumberingEnvBeginRegex = new Regex(
            @"\\begin\{(equation|align|gather)(\*)?\}", RegexOptions.Compiled);
        private static readonly Regex NumberingSuppressionRegex = new Regex(
            @"\\(?:nonumber|notag)\b", RegexOptions.Compiled);
        private static readonly Regex LineBreakRegex = new Regex(
            @"(?<!\\)\\\\(?![a-zA-Z])", RegexOptions.Compiled);

        public static bool IsAutoNumberedLatex(string latex)
        {
            return GetAutoNumberLineCount(latex) > 0;
        }

        /// <summary>
        /// Counts auto-numbered equation lines based on environment and tag/nonumber markers.
        /// </summary>
        public static int GetAutoNumberLineCount(string latex)
        {
            ParsedNumberingEnvironment parsed;
            if (!TryParseNumberingEnvironment(latex, out parsed) || parsed.IsStarred)
            {
                return 0;
            }

            if (IsPerLineNumberingEnvironment(parsed.EnvironmentName))
            {
                int count = 0;
                var lines = SplitEnvironmentLines(parsed.Content);
                foreach (var line in lines)
                {
                    var info = AnalyzeLineNumbering(line);
                    if (!info.HasCustomTag && !info.SuppressAutoNumber)
                    {
                        count++;
                    }
                }

                return count;
            }

            // equation/multline are single-number environments.
            var envInfo = AnalyzeLineNumbering(parsed.Content);
            if (envInfo.HasCustomTag || envInfo.SuppressAutoNumber)
            {
                return 0;
            }

            return 1;
        }

        public static string BuildNumberedLatex(string latex, int startNumber, out int consumedCount)
        {
            consumedCount = 0;
            ParsedNumberingEnvironment parsed;
            if (!TryParseNumberingEnvironment(latex, out parsed) || parsed.IsStarred)
            {
                return latex;
            }

            if (!IsPerLineNumberingEnvironment(parsed.EnvironmentName))
            {
                int singleNumber = Math.Max(1, startNumber);
                var info = AnalyzeLineNumbering(parsed.Content);
                var cleanedContent = StripLineNumberingCommands(parsed.Content).TrimEnd();
                string tagCommand = null;

                if (info.HasCustomTag)
                {
                    tagCommand = BuildTagCommand(info.CustomTagContent, info.CustomTagStarred);
                }
                else if (!info.SuppressAutoNumber)
                {
                    tagCommand = BuildTagCommand(singleNumber.ToString(), false);
                    consumedCount = 1;
                }

                if (consumedCount <= 0)
                {
                    return latex;
                }

                string singleBeginReplacement = "\\begin{" + parsed.EnvironmentName + "*}";
                string singleEndReplacement = "\\end{" + parsed.EnvironmentName + "*}";
                var singleBuilder = new StringBuilder();
                singleBuilder.Append(latex.Substring(0, parsed.BeginIndex));
                singleBuilder.Append(singleBeginReplacement);
                singleBuilder.Append(AppendTagToLine(cleanedContent, tagCommand));
                singleBuilder.Append(singleEndReplacement);
                singleBuilder.Append(latex.Substring(parsed.EndIndex + parsed.EndTokenLength));
                return singleBuilder.ToString();
            }

            var lines = SplitEnvironmentLines(parsed.Content);
            if (lines.Count == 0)
            {
                return latex;
            }

            int nextNumber = Math.Max(1, startNumber);
            var rebuiltLines = new List<string>(lines.Count);

            foreach (var line in lines)
            {
                var info = AnalyzeLineNumbering(line);
                var cleanedLine = StripLineNumberingCommands(line).TrimEnd();
                string tagCommand = null;

                if (info.HasCustomTag)
                {
                    tagCommand = BuildTagCommand(info.CustomTagContent, info.CustomTagStarred);
                }
                else if (!info.SuppressAutoNumber)
                {
                    tagCommand = BuildTagCommand(nextNumber.ToString(), false);
                    nextNumber++;
                    consumedCount++;
                }

                rebuiltLines.Add(AppendTagToLine(cleanedLine, tagCommand));
            }

            if (consumedCount <= 0)
            {
                return latex;
            }

            string beginReplacement = "\\begin{" + parsed.EnvironmentName + "*}";
            string endReplacement = "\\end{" + parsed.EnvironmentName + "*}";
            var builder = new StringBuilder();
            builder.Append(latex.Substring(0, parsed.BeginIndex));
            builder.Append(beginReplacement);
            builder.Append(string.Join(@"\\", rebuiltLines.ToArray()));
            builder.Append(endReplacement);
            builder.Append(latex.Substring(parsed.EndIndex + parsed.EndTokenLength));
            return builder.ToString();
        }

        public static bool IsPerLineNumberingEnvironment(string environmentName)
        {
            return string.Equals(environmentName, "align", StringComparison.Ordinal)
                || string.Equals(environmentName, "gather", StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses the first supported numbering environment and its exact begin/end offsets.
        /// </summary>
        public static bool TryParseNumberingEnvironment(string latex, out ParsedNumberingEnvironment parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(latex))
            {
                return false;
            }

            var beginMatch = NumberingEnvBeginRegex.Match(latex);
            if (!beginMatch.Success)
            {
                return false;
            }

            string environmentName = beginMatch.Groups[1].Value;
            bool isStarred = beginMatch.Groups[2].Success;
            string endToken = "\\end{" + environmentName + (isStarred ? "*" : string.Empty) + "}";
            int endIndex = latex.IndexOf(endToken, beginMatch.Index + beginMatch.Length, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                string fallbackEndToken = "\\end{" + environmentName + "}";
                endIndex = latex.IndexOf(fallbackEndToken, beginMatch.Index + beginMatch.Length, StringComparison.Ordinal);
                if (endIndex < 0)
                {
                    fallbackEndToken = "\\end{" + environmentName + "*}";
                    endIndex = latex.IndexOf(fallbackEndToken, beginMatch.Index + beginMatch.Length, StringComparison.Ordinal);
                    if (endIndex < 0)
                    {
                        return false;
                    }
                }

                endToken = fallbackEndToken;
                isStarred = endToken.EndsWith("*}", StringComparison.Ordinal);
            }

            int contentStart = beginMatch.Index + beginMatch.Length;
            parsed = new ParsedNumberingEnvironment
            {
                BeginIndex = beginMatch.Index,
                BeginTokenLength = beginMatch.Length,
                EndIndex = endIndex,
                EndTokenLength = endToken.Length,
                EnvironmentName = environmentName,
                IsStarred = isStarred,
                Content = latex.Substring(contentStart, endIndex - contentStart)
            };
            return true;
        }

        public static List<string> SplitEnvironmentLines(string content)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(content))
            {
                lines.Add(string.Empty);
                return lines;
            }

            var parts = LineBreakRegex.Split(content);
            if (parts.Length == 0)
            {
                lines.Add(content);
                return lines;
            }

            lines.AddRange(parts);
            return lines;
        }

        /// <summary>
        /// Extracts numbering directives on a single logical line (tag/nonumber/notag).
        /// </summary>
        public static LineNumberingInfo AnalyzeLineNumbering(string line)
        {
            var info = new LineNumberingInfo
            {
                SuppressAutoNumber = NumberingSuppressionRegex.IsMatch(line ?? string.Empty)
            };

            int searchStart = 0;
            int tagStart;
            while (TryFindTagCommand(line, searchStart, out tagStart))
            {
                int tagEnd;
                string tagContent;
                bool isTagStarred;
                if (TryParseTagCommand(line, tagStart, out tagEnd, out tagContent, out isTagStarred))
                {
                    info.HasCustomTag = true;
                    info.CustomTagContent = tagContent;
                    info.CustomTagStarred = isTagStarred;
                    return info;
                }

                searchStart = tagStart + 4;
            }

            return info;
        }

        /// <summary>
        /// Removes numbering directives so renumbering can rebuild deterministic tag commands.
        /// </summary>
        private static string StripLineNumberingCommands(string line)
        {
            string cleaned = NumberingSuppressionRegex.Replace(line ?? string.Empty, string.Empty);
            int searchStart = 0;
            int cursor = 0;
            StringBuilder builder = null;

            int tagStart;
            while (TryFindTagCommand(cleaned, searchStart, out tagStart))
            {
                int tagEnd;
                string tagContent;
                bool isTagStarred;
                if (!TryParseTagCommand(cleaned, tagStart, out tagEnd, out tagContent, out isTagStarred))
                {
                    searchStart = tagStart + 4;
                    continue;
                }

                if (builder == null)
                {
                    builder = new StringBuilder(cleaned.Length);
                }

                builder.Append(cleaned, cursor, tagStart - cursor);
                cursor = tagEnd;
                searchStart = tagEnd;
            }

            if (builder == null)
            {
                return cleaned;
            }

            builder.Append(cleaned, cursor, cleaned.Length - cursor);
            return builder.ToString();
        }

        private static bool TryFindTagCommand(string line, int startIndex, out int tagStart)
        {
            tagStart = -1;
            if (string.IsNullOrEmpty(line) || startIndex >= line.Length)
            {
                return false;
            }

            for (int i = Math.Max(0, startIndex); i <= line.Length - 4; i++)
            {
                if (line[i] != '\\')
                {
                    continue;
                }

                if (line[i + 1] != 't' || line[i + 2] != 'a' || line[i + 3] != 'g')
                {
                    continue;
                }

                int next = i + 4;
                if (next < line.Length && char.IsLetter(line[next]))
                {
                    continue;
                }

                tagStart = i;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses a \tag or \tag* command, including nested braces, and returns the parse span.
        /// </summary>
        private static bool TryParseTagCommand(
            string line,
            int tagStart,
            out int tagEnd,
            out string tagContent,
            out bool isTagStarred)
        {
            tagEnd = -1;
            tagContent = string.Empty;
            isTagStarred = false;

            if (string.IsNullOrEmpty(line) || tagStart < 0 || tagStart + 4 > line.Length)
            {
                return false;
            }

            int index = tagStart + 4;
            if (index < line.Length && line[index] == '*')
            {
                isTagStarred = true;
                index++;
            }

            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (index >= line.Length || line[index] != '{')
            {
                return false;
            }

            int contentStart = index + 1;
            int depth = 1;
            index++;
            while (index < line.Length && depth > 0)
            {
                if (line[index] == '{')
                {
                    depth++;
                }
                else if (line[index] == '}')
                {
                    depth--;
                }

                index++;
            }

            if (depth != 0)
            {
                return false;
            }

            tagEnd = index;
            tagContent = line.Substring(contentStart, index - contentStart - 1);
            return true;
        }

        private static string BuildTagCommand(string content, bool starred)
        {
            return "\\tag" + (starred ? "*" : string.Empty) + "{" + (content ?? string.Empty) + "}";
        }

        private static string AppendTagToLine(string line, string tagCommand)
        {
            if (string.IsNullOrEmpty(tagCommand))
            {
                return line ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return tagCommand;
            }

            return line.TrimEnd() + " " + tagCommand;
        }
    }
}
