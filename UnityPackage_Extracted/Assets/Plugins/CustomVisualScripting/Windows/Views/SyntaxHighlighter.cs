using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CustomVisualScripting.Windows.Views
{
    /// <summary>
    /// Простой однопроходный токенизатор C# для IMGUI rich-text подсветки.
    /// Возвращает строку с тегами &lt;color=...&gt;...&lt;/color&gt;.
    /// </summary>
    internal static class SyntaxHighlighter
    {
        // VSCode Dark+ палитра
        internal static readonly Color DefaultTextColor = new Color(0.831f, 0.831f, 0.831f);
        private  static readonly Color KeywordColor     = new Color(0.337f, 0.612f, 0.839f);
        private  static readonly Color TypeColor        = new Color(0.306f, 0.788f, 0.690f);
        private  static readonly Color StringColor      = new Color(0.808f, 0.569f, 0.471f);
        private  static readonly Color CommentColor     = new Color(0.416f, 0.600f, 0.333f);
        private  static readonly Color NumberColor      = new Color(0.710f, 0.808f, 0.659f);

        private static readonly HashSet<string> s_keywords = new(StringComparer.Ordinal)
        {
            "abstract","as","base","break","case","catch","checked","class","const","continue",
            "default","delegate","do","else","enum","event","explicit","extern","false","finally",
            "fixed","for","foreach","goto","if","implicit","in","interface","internal","is",
            "lock","namespace","new","null","operator","out","override","params","private",
            "protected","public","readonly","ref","return","sealed","sizeof","stackalloc",
            "static","struct","switch","this","throw","true","try","typeof","unchecked",
            "unsafe","using","virtual","volatile","while"
        };

        private static readonly HashSet<string> s_typeKeywords = new(StringComparer.Ordinal)
        {
            "bool","byte","char","decimal","double","float","int","long","object",
            "sbyte","short","string","uint","ulong","ushort","void","var",
            // Встроенные типы Unity-API (категория "Unity" в Create Node)
            "Vector3","Vector2","GameObject","Transform","Mathf","Input","Time","Random","Debug"
        };

        private static readonly StringBuilder s_sb = new();

        // ---------------------------------------------------------------

        /// <summary>
        /// Возвращает rich-text строку с тегами &lt;color&gt; для IMGUI GUI.Label.
        /// nodeVarColors: variableName → Color (цвет обводки ноды).
        /// </summary>
        public static string BuildHighlightedText(
            string code,
            IReadOnlyDictionary<string, Color> nodeVarColors)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;

            var spans = Tokenize(code, nodeVarColors);
            return AssembleRichText(code, spans);
        }

        // ---------------------------------------------------------------

        private readonly struct Span
        {
            public readonly int Start, End;
            public readonly Color Color;
            public Span(int s, int e, Color c) { Start = s; End = e; Color = c; }
        }

        private static List<Span> Tokenize(string code, IReadOnlyDictionary<string, Color> nodeVarColors)
        {
            var out_ = new List<Span>();
            int i = 0, n = code.Length;

            while (i < n)
            {
                char c = code[i];

                // Однострочный комментарий //
                if (c == '/' && i + 1 < n && code[i + 1] == '/')
                {
                    int s = i;
                    while (i < n && code[i] != '\n') i++;
                    out_.Add(new Span(s, i, CommentColor));
                    continue;
                }

                // Блочный комментарий /* ... */
                if (c == '/' && i + 1 < n && code[i + 1] == '*')
                {
                    int s = i; i += 2;
                    while (i + 1 < n && !(code[i] == '*' && code[i + 1] == '/')) i++;
                    i = Math.Min(i + 2, n);
                    out_.Add(new Span(s, i, CommentColor));
                    continue;
                }

                // Verbatim-строка @"..."
                if (c == '@' && i + 1 < n && code[i + 1] == '"')
                {
                    int s = i; i += 2;
                    while (i < n)
                    {
                        if (code[i] == '"')
                        {
                            i++;
                            if (i < n && code[i] == '"') { i++; continue; } // escaped ""
                            break;
                        }
                        i++;
                    }
                    out_.Add(new Span(s, i, StringColor));
                    continue;
                }

                // Обычная строка "..."
                if (c == '"')
                {
                    int s = i++;
                    while (i < n && code[i] != '"' && code[i] != '\n')
                    {
                        if (code[i] == '\\' && i + 1 < n) i++;
                        i++;
                    }
                    if (i < n && code[i] == '"') i++;
                    out_.Add(new Span(s, i, StringColor));
                    continue;
                }

                // Символьный литерал '.'
                if (c == '\'')
                {
                    int s = i++;
                    while (i < n && code[i] != '\'' && code[i] != '\n')
                    {
                        if (code[i] == '\\' && i + 1 < n) i++;
                        i++;
                    }
                    if (i < n && code[i] == '\'') i++;
                    out_.Add(new Span(s, i, StringColor));
                    continue;
                }

                // Числовой литерал (начинается с цифры)
                if (char.IsDigit(c))
                {
                    int s = i;
                    // Hex: 0x...
                    if (c == '0' && i + 1 < n && (code[i + 1] == 'x' || code[i + 1] == 'X'))
                    {
                        i += 2;
                        while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                    }
                    else
                    {
                        while (i < n && (char.IsDigit(code[i]) || code[i] == '.' || code[i] == '_')) i++;
                        // Суффиксы: f, d, m, L, u, ul, ...
                        while (i < n && "fFdDmMlLuU".IndexOf(code[i]) >= 0) i++;
                    }
                    out_.Add(new Span(s, i, NumberColor));
                    continue;
                }

                // Идентификатор / ключевое слово
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                    string word = code.Substring(s, i - s);

                    Color col = default;
                    bool hit = false;

                    if (nodeVarColors != null && nodeVarColors.TryGetValue(word, out col))
                        hit = true;
                    else if (s_typeKeywords.Contains(word)) { col = TypeColor;    hit = true; }
                    else if (s_keywords.Contains(word))    { col = KeywordColor; hit = true; }

                    if (hit) out_.Add(new Span(s, i, col));
                    continue;
                }

                i++;
            }

            return out_;
        }

        // ---------------------------------------------------------------

        private static string AssembleRichText(string code, List<Span> spans)
        {
            var sb = s_sb;
            sb.Clear();
            int pos = 0;

            foreach (var sp in spans)
            {
                // Текст между токенами — цвет по умолчанию (без тега)
                if (sp.Start > pos)
                    sb.Append(code, pos, sp.Start - pos);

                sb.Append("<color=").Append(ToHex(sp.Color)).Append('>');
                sb.Append(code, sp.Start, sp.End - sp.Start);
                sb.Append("</color>");
                pos = sp.End;
            }

            // Хвост
            if (pos < code.Length)
                sb.Append(code, pos, code.Length - pos);

            return sb.ToString();
        }

        internal static string ToHex(Color c) =>
            $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";
    }
}
