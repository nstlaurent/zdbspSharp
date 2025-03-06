namespace zdbspSharp;

public static class StringExtensions
{
    public static bool strnicmp(byte[] string1, string string2, int n)
    {
        for (int i = 0; i < n && i < string2.Length; i++)
        {
            if (string1[i] == 0)
                return false;

            if (char.ToLowerInvariant((char)string1[i]) != char.ToLowerInvariant(string2[i]))
                return false;
        }

        return true;
    }

    public static void CopyString(byte[] dest, string str, int length)
    {
        for (int i = 0; i < length && i < str.Length; i++)
            dest[i] = (byte)str[i];
    }

    public static bool EqualsIgnoreCase(this string str, string other) => 
        str.Equals(other, StringComparison.OrdinalIgnoreCase);
    public static bool EqualsIgnoreCase(this ReadOnlySpan<char> str, string other) =>
        str.Equals(other, StringComparison.OrdinalIgnoreCase);
}
