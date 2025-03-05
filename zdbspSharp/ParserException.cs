namespace zdbspSharp;

/// <summary>
/// An exception thrown by the parser.
/// </summary>
internal class ParserException : Exception
{
    /// <summary>
    /// The line number in the source file that the parsing error occurred
    /// at. This starts at 1 (should never be zero).
    /// </summary>
    public readonly int LineNumber;

    /// <summary>
    /// The offset in characters from the start of the line. This starts
    /// from zero.
    /// </summary>
    public readonly int LineCharOffset;

    /// <summary>
    /// The offset in characters from the beginning of the text stream
    /// before it was tokenized. This starts from zero.
    /// </summary>
    public readonly int CharOffset;

    /// <summary>
    /// Creates a parser exception at some point in a text stream.
    /// </summary>
    /// <param name="lineNumber">The line number.</param>
    /// <param name="lineCharOffset">The character offset at the line.
    /// </param>
    /// <param name="charOffset">The character offset from the character
    /// stream.</param>
    /// <param name="message">The error message.</param>
    public ParserException(int lineNumber, int lineCharOffset, int charOffset, string message) : base(message)
    {
        LineNumber = lineNumber;
        LineCharOffset = lineCharOffset;
        CharOffset = charOffset;
    }

    private static int CalculateLeftIndex(string text, int originalIndex)
    {
        int startIndex = originalIndex;
        for (; startIndex > 0; startIndex--)
        {
            if (text[startIndex] == '\n')
            {
                startIndex++;
                break;
            }
        }

        // This keeps us in some reasonable range. We also don't need
        // to worry about it going negative because startIndex should
        // never go negative due to the loop exiting before that.
        return Math.Clamp(startIndex, originalIndex - 128, originalIndex);
    }

    private static int CalculateRightNonInclusiveIndex(string text, int originalIndex)
    {
        int endIndex = originalIndex;
        for (; endIndex < text.Length; endIndex++)
        {
            if (text[endIndex] == '\n' || text[endIndex] == '\r')
            {
                endIndex--;
                break;
            }
        }

        // This keeps us in some reasonable range. We also don't need
        // to worry about it going negative because startIndex should
        // never go negative due to the loop exiting before that.
        return Math.Clamp(endIndex, originalIndex, originalIndex + 128);
    }

    private void LogContextualInformation(string text, List<string> errorMessages)
    {
        int leftIndex = CalculateLeftIndex(text, CharOffset);
        int rightIndexNonInclusive = CalculateRightNonInclusiveIndex(text, CharOffset);

        if (leftIndex >= rightIndexNonInclusive)
        {
            errorMessages.Add("Parsing error occurred on a blank line with no text, no contextual information available");
            return;
        }

        int numSpaces = CharOffset - leftIndex;
        int substringLength = rightIndexNonInclusive - leftIndex + 1;
        string textContext = text.Substring(leftIndex, substringLength);
        string caret = new string(' ', numSpaces) + "^";

        errorMessages.Add(textContext);
        errorMessages.Add(caret);
    }
}
