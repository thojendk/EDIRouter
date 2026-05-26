using System.Text;

namespace EDIRouter;

public sealed class EdifactInfo
{
    public string Recipient { get; init; } = string.Empty;
    public bool IsTest { get; init; }
}

public static class EdiParser
{
    // Reads just enough of the file to extract the UNB segment.
    // Returns null if the file cannot be parsed as a valid EDIFACT interchange.
    public static EdifactInfo? ParseUnb(string filePath)
    {
        byte[] buffer = new byte[2048];
        int bytesRead;

        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bytesRead = stream.Read(buffer, 0, buffer.Length);
        }

        // Try UTF-8 first, fall back to Latin-1
        string content;
        try { content = Encoding.UTF8.GetString(buffer, 0, bytesRead); }
        catch { content = Encoding.Latin1.GetString(buffer, 0, bytesRead); }

        // Default EDIFACT service characters
        char componentSep = ':';
        char dataElementSep = '+';
        char segmentTerminator = '\'';
        char releaseChar = '?';

        int searchFrom = 0;

        if (content.StartsWith("UNA", StringComparison.Ordinal) && content.Length >= 9)
        {
            componentSep = content[3];
            dataElementSep = content[4];
            releaseChar = content[6];
            segmentTerminator = content[8];
            searchFrom = 9;
        }

        int unbStart = content.IndexOf("UNB", searchFrom, StringComparison.Ordinal);
        if (unbStart < 0)
            return null;

        int unbEnd = FindSegmentEnd(content, unbStart, segmentTerminator, releaseChar);
        if (unbEnd < 0)
            return null;

        string unbSegment = content[unbStart..unbEnd];
        var fields = Split(unbSegment, dataElementSep, releaseChar);

        // fields[0]="UNB", [1]=syntax, [2]=sender, [3]=recipient, [11]=test indicator
        if (fields.Count < 4)
            return null;

        var recipientParts = Split(fields[3], componentSep, releaseChar);
        string recipient = recipientParts[0].Trim();
        if (string.IsNullOrEmpty(recipient))
            return null;

        bool isTest = fields.Count > 11 && fields[11].Trim() == "1";

        return new EdifactInfo { Recipient = recipient, IsTest = isTest };
    }

    private static int FindSegmentEnd(string content, int start, char terminator, char releaseChar)
    {
        bool escaped = false;
        for (int i = start; i < content.Length; i++)
        {
            if (escaped) { escaped = false; continue; }
            if (content[i] == releaseChar) { escaped = true; continue; }
            if (content[i] == terminator) return i;
        }
        return -1;
    }

    private static List<string> Split(string input, char separator, char releaseChar)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool escaped = false;

        foreach (char c in input)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
            }
            else if (c == releaseChar)
            {
                escaped = true;
            }
            else if (c == separator)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
