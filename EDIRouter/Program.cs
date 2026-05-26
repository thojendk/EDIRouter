using EDIRouter;

if (args.Length < 2)
{
    Console.WriteLine("EDIRouter - Route EDIFACT files by recipient");
    Console.WriteLine();
    Console.WriteLine("Usage: EDIRouter <input-folder> <output-folder>");
    Console.WriteLine();
    Console.WriteLine("  input-folder   Folder containing EDIFACT interchange files");
    Console.WriteLine("  output-folder  Root folder for routed output");
    Console.WriteLine();
    Console.WriteLine("Files are moved to: <output-folder>\\<recipient>\\<test|prod>\\");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];

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
Console.WriteLine();

var processor = new FileProcessor(inputPath, outputPath, logger);
processor.ProcessAll();

return 0;
