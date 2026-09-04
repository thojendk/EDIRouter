using EDIRouter;

var positional = new List<string>();
string? splitQualifier = null;   // NAD party qualifier to route by; null = no split
string? argError = null;

foreach (string arg in args)
{
    if (arg == "--split")
    {
        splitQualifier = "SU";
    }
    else if (arg.StartsWith("--split=", StringComparison.Ordinal))
    {
        string value = arg["--split=".Length..].Trim().ToUpperInvariant();
        if (value.Length is < 1 or > 3 || !value.All(char.IsAsciiLetterOrDigit))
            argError = $"Invalid NAD qualifier in '{arg}'. Expected 1-3 alphanumeric characters, e.g. --split=SU or --split=BY.";
        else
            splitQualifier = value;
    }
    else
    {
        positional.Add(arg);
    }
}

if (argError is not null)
{
    Console.Error.WriteLine($"Error: {argError}");
    return 2;
}

if (positional.Count < 2)
{
    Console.WriteLine("EDIRouter - Route EDIFACT files by recipient");
    Console.WriteLine();
    Console.WriteLine("Usage: EDIRouter <input-folder> <output-folder> [--split[=<nad>]]");
    Console.WriteLine();
    Console.WriteLine("  input-folder     Folder containing EDIFACT interchange files");
    Console.WriteLine("  output-folder    Root folder for routed output");
    Console.WriteLine("  --split[=<nad>]  Route by the GLN in a NAD segment. <nad> is the party");
    Console.WriteLine("                   qualifier to route on: SU (supplier, the default),");
    Console.WriteLine("                   BY (buyer), and so on. Files carrying more than one");
    Console.WriteLine("                   GLN are split into one interchange per GLN (UNZ");
    Console.WriteLine("                   count corrected).");
    Console.WriteLine();
    Console.WriteLine("Files are moved to: <output-folder>\\<recipient>\\<test|prod>\\");
    Console.WriteLine("With --split:       <output-folder>\\<recipient>\\<test|prod>\\<gln>\\");
    return 1;
}

string inputPath = positional[0];
string outputPath = positional[1];

if (!Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Error: Input folder does not exist: {inputPath}");
    return 2;
}

string logDirectory = Path.Combine(outputPath, "logs");
var logger = new Logger(logDirectory);
logger.PurgeOldLogs();

Console.WriteLine($"EDIRouter");
Console.WriteLine($"  Input:  {inputPath}");
Console.WriteLine($"  Output: {outputPath}");
Console.WriteLine($"  Split:  {(splitQualifier is null ? "off" : $"on (by NAD+{splitQualifier} GLN)")}");
Console.WriteLine();

var processor = new FileProcessor(inputPath, outputPath, splitQualifier, logger);
processor.ProcessAll();

return 0;
