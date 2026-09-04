using System.Text;

namespace EDIRouter;

public sealed class EdifactInfo
{
    public string Recipient { get; init; } = string.Empty;
    public bool IsTest { get; init; }
}

// A single message (UNH..UNT inclusive), captured as raw text so it can be
// re-emitted byte-for-byte, along with the party GLN taken from the NAD segment
// carrying the routing qualifier (SU, BY, ...).
public sealed class EdiMessage
{
    public string Raw { get; init; } = string.Empty;
    public string? PartyGln { get; init; }
}

// One output interchange produced by grouping messages by party GLN.
public sealed class EdiSplitGroup
{
    public string Gln { get; init; } = string.Empty;   // "unknown" when the NAD segment is absent
    public string Content { get; init; } = string.Empty;
    public int MessageCount { get; init; }
}

// A fully parsed interchange, retaining enough raw text and service characters
// to rebuild one or more interchanges when splitting by GLN.
public sealed class EdiInterchange
{
    public string Recipient { get; init; } = string.Empty;
    public bool IsTest { get; init; }
    public string Preamble { get; init; } = string.Empty;   // UNA segment + layout (may be "")
    public string UnbRaw { get; init; } = string.Empty;     // UNB incl. terminator + layout
    public IReadOnlyList<EdiMessage> Messages { get; init; } = [];
    public char DataElementSep { get; init; } = '+';
    public char SegmentTerminator { get; init; } = '\'';
    public string UnzRef { get; init; } = string.Empty;     // control reference from UNZ
    public string UnzTrailing { get; init; } = string.Empty; // layout following the UNZ terminator
    public Encoding Encoding { get; init; } = Encoding.UTF8;

    // Groups messages by party GLN (null => "unknown"), preserving first-seen
    // order. Always returns at least one group; the caller splits only when
    // there is more than one.
    public IReadOnlyList<EdiSplitGroup> GroupByGln()
    {
        var order = new List<string>();
        var map = new Dictionary<string, List<EdiMessage>>(StringComparer.Ordinal);

        foreach (var message in Messages)
        {
            string key = message.PartyGln ?? "unknown";
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
                order.Add(key);
            }
            list.Add(message);
        }

        var groups = new List<EdiSplitGroup>(order.Count);
        foreach (string key in order)
        {
            var msgs = map[key];
            var body = new StringBuilder();
            foreach (var m in msgs)
                body.Append(m.Raw);

            string content = Preamble + UnbRaw + body + BuildUnz(msgs.Count) + UnzTrailing;
            groups.Add(new EdiSplitGroup { Gln = key, Content = content, MessageCount = msgs.Count });
        }
        return groups;
    }

    private string BuildUnz(int count)
        => $"UNZ{DataElementSep}{count}{DataElementSep}{UnzRef}{SegmentTerminator}";
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

    // Reads the entire file and parses it into its UNB header, individual
    // messages, and UNZ trailer, retaining raw text so the interchange can be
    // re-emitted (or split) without losing the original layout.
    // nadQualifier selects which NAD party (3035) supplies each message's
    // routing GLN - "SU" for the supplier, "BY" for the buyer, and so on.
    // Returns null if the file is not a usable single EDIFACT interchange.
    public static EdiInterchange? ParseInterchange(string filePath, string nadQualifier)
    {
        byte[] bytes = File.ReadAllBytes(filePath);

        Encoding encoding;
        string content;
        try
        {
            content = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            encoding = new UTF8Encoding(false); // write back without BOM
        }
        catch
        {
            encoding = Encoding.Latin1;
            content = encoding.GetString(bytes);
        }

        char componentSep = ':';
        char dataElementSep = '+';
        char segmentTerminator = '\'';
        char releaseChar = '?';

        int pos = 0;
        string preamble = string.Empty;

        if (content.StartsWith("UNA", StringComparison.Ordinal) && content.Length >= 9)
        {
            componentSep = content[3];
            dataElementSep = content[4];
            releaseChar = content[6];
            segmentTerminator = content[8];
            int p = 9;
            while (p < content.Length && IsLayoutWhitespace(content[p])) p++;
            preamble = content[0..p];
            pos = p;
        }

        string? unbRaw = null;
        string recipient = string.Empty;
        bool isTest = false;
        var messages = new List<EdiMessage>();
        string unzRef = string.Empty;
        string unzTrailing = string.Empty;
        bool foundUnz = false;

        List<string>? currentMsg = null;
        string? currentGln = null;

        while (pos < content.Length)
        {
            if (IsLayoutWhitespace(content[pos])) { pos++; continue; }
            if (pos + 3 > content.Length) break;

            string tag = content.Substring(pos, 3);
            int end = FindSegmentEnd(content, pos, segmentTerminator, releaseChar);
            if (end < 0) break;

            int rawEnd = end + 1;
            int q = rawEnd;
            while (q < content.Length && IsLayoutWhitespace(content[q])) q++;

            string raw = content[pos..q];          // segment incl. terminator + trailing layout
            string segData = content[pos..end];    // segment fields without terminator

            switch (tag)
            {
                case "UNB":
                    unbRaw = raw;
                    var ub = Split(segData, dataElementSep, releaseChar);
                    if (ub.Count >= 4)
                    {
                        var recipientParts = Split(ub[3], componentSep, releaseChar);
                        recipient = recipientParts[0].Trim();
                        isTest = ub.Count > 11 && ub[11].Trim() == "1";
                    }
                    break;

                case "UNH":
                    currentMsg = [raw];
                    currentGln = null;
                    break;

                case "UNT":
                    if (currentMsg is not null)
                    {
                        currentMsg.Add(raw);
                        messages.Add(new EdiMessage
                        {
                            Raw = string.Concat(currentMsg),
                            PartyGln = currentGln
                        });
                        currentMsg = null;
                    }
                    break;

                case "UNZ":
                    var uz = Split(segData, dataElementSep, releaseChar);
                    unzRef = uz.Count > 2 ? uz[2].Trim() : string.Empty;
                    unzTrailing = content[rawEnd..q];
                    foundUnz = true;
                    break;

                default:
                    if (currentMsg is not null)
                    {
                        currentMsg.Add(raw);
                        if (tag == "NAD" && currentGln is null)
                        {
                            var nad = Split(segData, dataElementSep, releaseChar);
                            if (nad.Count > 2 && nad[1].Trim() == nadQualifier)
                            {
                                var comps = Split(nad[2], componentSep, releaseChar);
                                string gln = comps[0].Trim();
                                if (!string.IsNullOrEmpty(gln))
                                    currentGln = gln;
                            }
                        }
                    }
                    break;
            }

            pos = q;
            if (foundUnz) break;
        }

        if (unbRaw is null || !foundUnz || messages.Count == 0 || string.IsNullOrEmpty(recipient))
            return null;

        return new EdiInterchange
        {
            Recipient = recipient,
            IsTest = isTest,
            Preamble = preamble,
            UnbRaw = unbRaw,
            Messages = messages,
            DataElementSep = dataElementSep,
            SegmentTerminator = segmentTerminator,
            UnzRef = unzRef,
            UnzTrailing = unzTrailing,
            Encoding = encoding
        };
    }

    private static bool IsLayoutWhitespace(char c)
        => c is ' ' or '\t' or '\r' or '\n';

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
