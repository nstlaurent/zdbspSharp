﻿using System.Globalization;
using System.Runtime.CompilerServices;

namespace zdbspSharp;

public enum ParseType
{
    Normal,
    Csv
}

public readonly record struct ParserOffset(int Line, int Char);
record struct LineSpan(int Index, int Length, int NextIndex);

internal class SimpleParser
{
    private class ParserToken(int line, int index, int length,
        int endLine = -1, int endIndex = -1)
    {
        public int Index = index;
        public int Line = line;
        public int Length = length;
        public int EndLine = endLine;
        public int EndIndex = endIndex;
    }

    private readonly List<ParserToken> m_tokens = [];
    private readonly HashSet<char> m_special = [];
    private readonly ParseType m_parseType;
    private readonly List<LineSpan> m_lines = [];
    private readonly bool m_keepBeginningSpaces;
    private Func<string, int, int, bool>? m_commentCallback;

    private int m_index = 0;
    private int m_startLine;
    private bool m_isQuote;
    private bool m_quotedString;
    private bool m_split;
    private string m_data = string.Empty;

    private static readonly NumberFormatInfo DecimalFormat = new() { NumberDecimalSeparator = "." };

    public static bool TryParseDouble(string text, out double d) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, DecimalFormat, out d);
    public static bool TryParseDouble(ReadOnlySpan<char> text, out double d) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, DecimalFormat, out d);

    public static bool TryParseFloat(string text, out float f) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, DecimalFormat, out f);
    public static bool TryParseFloat(ReadOnlySpan<char> text, out float f) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, DecimalFormat, out f);

    private static readonly char[] SpecialChars = ['{', '}', '=', ';', ',', '[', ']'];

    public SimpleParser(ParseType parseType = ParseType.Normal, bool keepBeginningSpaces = false)
    {
        m_parseType = parseType;
        m_keepBeginningSpaces = keepBeginningSpaces;
        SetSpecialChars(SpecialChars);
    }

    public void SetSpecialChars(IEnumerable<char> special)
    {
        m_special.Clear();
        foreach (char c in special)
            m_special.Add(c);
    }

    public void SetCommentCallback(Func<string, int, int, bool> callback) =>
        m_commentCallback = callback;

    public void Parse(string data, bool keepEmptyLines = false, bool parseQuotes = true)
    {
        m_data = data;
        m_index = 0;
        m_startLine = 0;
        m_isQuote = false;
        m_quotedString = false;
        m_split = false;
        bool multiLineComment = false;
        int lineCount = 0;
        int startIndex = 0;
        int saveStartIndex = 0;
        int lineStartIndex = 0;

        m_tokens.EnsureCapacity(data.Length / 8);
        if (m_keepBeginningSpaces)
            m_lines.EnsureCapacity(data.Length / 16);

        for (int i = 0; i < data.Length; i++)
        {
            bool newLine = data[i] == '\n';
            bool lineReturn = i < data.Length - 1 && data[i] == '\r' && data[i + 1] == '\n';
            if (newLine || lineReturn)
            {
                AddEndLineToken(keepEmptyLines, multiLineComment, lineCount, startIndex, saveStartIndex, lineStartIndex, i);

                lineCount++;
                if (lineReturn)
                    i++;
                startIndex = i + 1;
                lineStartIndex = startIndex;

                if (!m_isQuote)
                    ResetQuote(lineCount);
                continue;
            }

            if (i >= data.Length)
                break;

            if (!m_isQuote && IsSingleLineComment(data, lineStartIndex - i, i))
            {
                if (i > 0)
                    AddToken(startIndex, i, lineCount, false);
                var lineSpan = GetLineSpan(data, startIndex);
                startIndex = lineSpan.NextIndex;
                lineStartIndex = startIndex;
                i = startIndex - 1;
                continue;
            }

            if (!m_isQuote && IsStartMultiLineComment(data, ref i))
                multiLineComment = true;

            if (multiLineComment && IsEndMultiLineComment(data, ref i))
            {
                multiLineComment = false;
                startIndex = i + 1;
                continue;
            }

            if (i >= data.Length)
                break;

            if (multiLineComment)
                continue;

            if (parseQuotes && data[i] == '"')
            {
                m_quotedString = true;
                m_isQuote = !m_isQuote;
                if (m_isQuote)
                {
                    AddToken(startIndex, i, lineCount, false);
                    saveStartIndex = i;
                }
                else
                {
                    m_split = true;
                }
            }

            if (!m_isQuote)
            {
                bool special = CheckSpecial(data[i]);
                if (m_split || special || CheckSplit(data[i]))
                {
                    if (m_startLine == lineCount)
                        AddToken(startIndex, i, lineCount, m_quotedString);
                    else
                        AddToken(saveStartIndex, m_startLine, lineCount, i, m_quotedString);
                    startIndex = i + 1;
                    m_split = false;

                    ResetQuote(lineCount);
                }

                // Also add the special char as a token (e.g. '{')
                if (special)
                    AddToken(i, i + 1, lineCount, m_quotedString);
            }
        }

        AddEndLineToken(keepEmptyLines, multiLineComment, lineCount, startIndex, saveStartIndex, lineStartIndex, data.Length);
    }

    private void AddEndLineToken(bool keepEmptyLines, bool multiLineComment, int lineCount, int startIndex, int saveStartIndex, int lineStartIndex, int i)
    {
        var lineSpan = new LineSpan(lineStartIndex, i - lineStartIndex, i + 1);
        if (lineSpan.Length == 0 && keepEmptyLines && !m_quotedString)
        {
            m_tokens.Add(new ParserToken(lineCount, lineStartIndex, 0));
        }
        else if (!m_isQuote && !multiLineComment)
        {
            if (m_startLine == lineCount)
                AddToken(startIndex, lineSpan.Index + lineSpan.Length, lineCount, m_quotedString);
            else if (lineSpan.Index + lineSpan.Length != startIndex)
                AddToken(saveStartIndex, m_startLine, lineCount, startIndex, m_quotedString);
        }

        if (m_keepBeginningSpaces)
            m_lines.Add(lineSpan);
    }

    private static LineSpan GetLineSpan(string data, int start)
    {
        int i = start;
        for (; i < data.Length; i++)
        {
            if (data[i] == '\n')
                return new LineSpan(start, i - start, i + 1);

            if (i < data.Length - 1 && data[i] == '\r' && data[i + 1] == '\n')
                return new LineSpan(start, i - start, i + 2);
        }

        return new LineSpan(start, i - start, i);
    }

    void ResetQuote(int lineCount)
    {
        m_isQuote = false;
        m_quotedString = false;
        m_split = false;
        m_startLine = lineCount;
    }

    // Just for debugging purposes
    public List<string> GetAllTokenStrings()
    {
        List<string> tokens = new(m_tokens.Count);
        for (int i = 0; i < m_tokens.Count; i++)
            tokens.Add(GetData(i));
        return tokens;
    }

    private static bool IsEndMultiLineComment(string data, ref int i)
    {
        if (i >= data.Length)
            return false;

        if (data[i] != '*' || !CheckNext(data, i, '/'))
            return false;

        i += 2;
        return true;
    }

    private static bool IsStartMultiLineComment(string data, ref int i)
    {
        if (data[i] != '/' || !CheckNext(data, i, '*'))
            return false;

        i += 2;
        return true;
    }

    private bool IsSingleLineComment(string data, int lineStartIndex, int index)
        => (m_commentCallback != null && m_commentCallback(data, lineStartIndex, index)) || (data[index] == '/' && CheckNext(data, index, '/'));

    private bool CheckSplit(char c)
    {
        if (m_parseType == ParseType.Normal)
            return c == ' ' || c == '\t';
        else
            return c == ',';
    }

    private bool CheckSpecial(char c)
    {
        if (m_parseType != ParseType.Normal)
            return false;

        return m_special.Contains(c);
    }

    private void AddToken(int startIndex, int currentIndex, int lineCount, bool quotedString)
    {
        if (quotedString)
        {
            startIndex--;
            currentIndex++;
        }

        // Always add empty string if in quotes
        if (quotedString || startIndex != currentIndex)
            m_tokens.Add(new ParserToken(lineCount, startIndex, currentIndex - startIndex));
    }

    private void AddToken(int startIndex, int startLine, int endLine, int endIndex, bool quotedString)
    {
        if (quotedString)
        {
            startIndex--;
            endIndex++;
        }

        m_tokens.Add(new ParserToken(startLine, startIndex, endIndex, endLine, endIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CheckNext(string str, int i, char c) => i + 1 < str.Length && str[i + 1] == c;

    public int GetCurrentLine() => IsDone() ? -1 : m_tokens[m_index].Line;
    public int GetCurrentCharOffset() => IsDone() ? -1 : m_tokens[m_index].Index;

    public ParserOffset GetCurrentOffset() => new(GetCurrentLine(), GetCurrentCharOffset());
    public bool IsDone() => m_index >= m_tokens.Count;

    public bool Peek(char c)
    {
        if (IsDone())
            return false;

        if (!GetCharData(m_index, out char getChar))
            return false;

        if (char.ToUpperInvariant(getChar) == char.ToUpperInvariant(c))
            return true;

        return false;
    }

    public bool Peek(string str)
    {
        if (IsDone())
            return false;

        if (GetDataSpan(m_index).Equals(str, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public ReadOnlySpan<char> PeekStringSpan()
    {
        if (IsDone())
            return string.Empty;

        AssertData();
        return GetDataSpan(m_index);
    }

    public string PeekString()
    {
        if (IsDone())
            return string.Empty;

        AssertData();
        return GetData(m_index);
    }

    public bool PeekString(int offset, out string? data)
    {
        data = null;
        if (m_index + offset >= m_tokens.Count)
            return false;

        data = GetData(m_index + offset);
        return true;
    }

    public bool PeekInteger(out int i)
    {
        if (IsDone())
        {
            i = 0;
            return false;
        }

        AssertData();
        return int.TryParse(GetDataSpan(m_index), out i);
    }

    public string ConsumeString()
    {
        AssertData();
        return GetData(m_index++);
    }

    public ReadOnlySpan<char> ConsumeStringSpan()
    {
        AssertData();
        return GetDataSpan(m_index++);
    }

    public void ConsumeString(string str)
    {
        AssertData();

        ParserToken token = m_tokens[m_index];
        var data = GetDataSpan(m_index);
        if (!data.Equals(str, StringComparison.OrdinalIgnoreCase))
            throw new ParserException(token.Line, token.Index, -1, $"Expected {str} but got {data}");

        m_index++;
    }

    public bool ConsumeIf(string str)
    {
        if (IsDone())
            return false;

        if (PeekStringSpan().Equals(str, StringComparison.OrdinalIgnoreCase))
        {
            ConsumeStringSpan();
            return true;
        }

        return false;
    }

    public int? ConsumeIfInt()
    {
        if (IsDone())
            return null;

        if (PeekInteger(out int i))
        {
            ConsumeStringSpan();
            return i;
        }

        return null;
    }

    public int ConsumeInteger()
    {
        AssertData();

        var token = m_tokens[m_index];
        var data = GetDataSpan(m_index);
        if (int.TryParse(data, out int i))
        {
            m_index++;
            return i;
        }

        throw new ParserException(token.Line, token.Index, -1, $"Could not parse {data} as integer.");
    }

    public double ConsumeDouble()
    {
        AssertData();

        var token = m_tokens[m_index];
        var data = GetDataSpan(m_index);
        if (TryParseDouble(data, out double d))
        {
            m_index++;
            return d;
        }

        throw new ParserException(token.Line, token.Index, -1, $"Could not parse {data} as a double.");
    }

    public bool ConsumeBool()
    {
        AssertData();

        ParserToken token = m_tokens[m_index];
        var data = GetDataSpan(m_index);
        if (bool.TryParse(data, out bool b))
        {
            m_index++;
            return b;
        }

        throw new ParserException(token.Line, token.Index, -1, $"Could not parse {data} as a bool.");
    }

    public double ParseDouble(ReadOnlySpan<char> data)
    {
        if (!TryParseDouble(data, out var d))
            throw new ParserException(GetCurrentLine(), -1, -1, $"Could not parse {data} as a double.");
        return d;
    }

    public float ParseFloat(ReadOnlySpan<char> data)
    {
        if (!TryParseFloat(data, out var d))
            throw new ParserException(GetCurrentLine(), -1, -1, $"Could not parse {data} as a float.");
        return d;
    }

    public int ParseInt(ReadOnlySpan<char> data)
    {
        if (!int.TryParse(data, out var d))
            throw new ParserException(GetCurrentLine(), -1, -1, $"Could not parse {data} as a int.");
        return d;
    }

    public void Consume(char c)
    {
        AssertData();

        ParserToken token = m_tokens[m_index];
        var data = GetDataSpan(m_index);
        if (data.Length != 1 || char.ToUpperInvariant(data[0]) != char.ToUpperInvariant(c))
            throw new ParserException(token.Line, token.Index, -1, $"Expected {c} but got {data}.");

        m_index++;
    }

    /// <summary>
    /// Eats the rest of the tokens until the current line is consumed.
    /// </summary>
    public string ConsumeLine(bool keepBeginningSpaces = false)
    {
        AssertData();

        var token = m_tokens[m_index];

        int startLine = m_tokens[m_index].Line;
        while (m_index < m_tokens.Count && m_tokens[m_index].Line == startLine)
            m_index++;

        if (m_keepBeginningSpaces && keepBeginningSpaces)
        {
            var lineSpan = m_lines[token.Line];
            return m_data.Substring(lineSpan.Index, lineSpan.Length);
        }

        var endToken = m_tokens[m_index - 1];
        return m_data.Substring(token.Index, endToken.Index + endToken.Length - token.Index);
    }

    public ReadOnlySpan<char> ConsumeLineSpan(bool keepBeginningSpaces = false)
    {
        AssertData();

        var token = m_tokens[m_index];

        int startLine = m_tokens[m_index].Line;
        while (m_index < m_tokens.Count && m_tokens[m_index].Line == startLine)
            m_index++;

        if (m_keepBeginningSpaces && keepBeginningSpaces)
        {
            var lineSpan = m_lines[token.Line];
            return m_data.Substring(lineSpan.Index, lineSpan.Length);
        }

        var endToken = m_tokens[m_index - 1];
        return m_data.Substring(token.Index, endToken.Index + endToken.Length - token.Index);
    }

    /// <summary>
    /// Returns all tokens until the next line is hit.
    /// </summary>
    public string PeekLine()
    {
        AssertData();
        int index = m_index;

        var token = m_tokens[index];
        int startLine = m_tokens[m_index].Line;
        while (index < m_tokens.Count - 1 && m_tokens[index].Line == startLine)
            index++;

        if (m_tokens[index].Line != startLine)
            index--;

        var endToken = m_tokens[index];
        return m_data.Substring(token.Index, endToken.Index - token.Index + endToken.Length);
    }

    public ParserException MakeException(string reason)
    {
        ParserToken token;
        if (m_index < m_tokens.Count)
            token = m_tokens[m_index];
        else
            token = m_tokens[^1];

        return new ParserException(token.Line, token.Index, 0, reason);
    }

    private void AssertData()
    {
        if (IsDone())
            throw new ParserException(GetCurrentLine(), GetCurrentCharOffset(), -1, "Hit end of file when expecting data.");
    }

    private ReadOnlySpan<char> GetDataSpan(int index)
    {
        var token = m_tokens[index];
        if (token.EndLine == -1)
            return m_data.AsSpan(token.Index, token.Length);
        else
            return m_data.AsSpan(token.Index, token.EndIndex - token.Index);
    }

    private string GetData(int index)
    {
        var token = m_tokens[index];
        if (token.EndLine == -1)
            return m_data.Substring(token.Index, token.Length);
        else
            return m_data.Substring(token.Index, token.EndIndex - token.Index);
    }

    private bool GetCharData(int index, out char c)
    {
        var token = m_tokens[index];
        if (token.Index >= m_data.Length)
        {
            c = ' ';
            return false;
        }

        c = m_data[token.Index];
        return true;
    }
}
