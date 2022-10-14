namespace zdbspSharp;

public static class StringExtensions
{
    public static int strnicmp(string string1, string string2, int n)
    {
        for (int i = 0; i < n && i < string1.Length; i++)
        {
            if (char.ToLowerInvariant(string1[i]) != char.ToLowerInvariant(string2[i]))
                return string1[i] - string2[i];
        }

        return 0;
    }

    public static void CopyString(byte[] dest, string str, int length)
    {
        for (int i = 0; i < length && i < str.Length; i++)
            dest[i] = (byte)str[i];
    }

    public static bool EqualsIgnoreCase(this string str, string other) => 
        str.Equals(other, StringComparison.OrdinalIgnoreCase);
}
