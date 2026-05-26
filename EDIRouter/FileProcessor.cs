namespace EDIRouter;

public sealed class FileProcessor(string inputPath, string outputPath, Logger logger)
{
    private readonly string _inputPath = inputPath;
    private readonly string _outputPath = outputPath;
    private readonly Logger _logger = logger;

    public void ProcessAll()
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(_inputPath);
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR  Could not read input directory '{_inputPath}': {ex.Message}");
            Console.Error.WriteLine($"Error: Could not read input directory: {ex.Message}");
            return;
        }

        int moved = 0, skipped = 0, failed = 0;

        foreach (string file in files)
        {
            var result = ProcessFile(file);
            switch (result)
            {
                case ProcessResult.Moved:    moved++;   break;
                case ProcessResult.Skipped:  skipped++; break;
                case ProcessResult.Failed:   failed++;  break;
            }
        }

        Console.WriteLine($"Done. Moved: {moved}  Skipped (locked): {skipped}  Failed: {failed}");
    }

    private ProcessResult ProcessFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        EdifactInfo? info;

        try
        {
            info = EdiParser.ParseUnb(filePath);
        }
        catch (IOException)
        {
            // File is locked — FTP transfer still in progress
            _logger.Log($"SKIP   {fileName}  (file is locked)");
            Console.WriteLine($"  Skipped (locked): {fileName}");
            return ProcessResult.Skipped;
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR  {fileName}  {ex.Message}");
            Console.Error.WriteLine($"  Error reading {fileName}: {ex.Message}");
            return ProcessResult.Failed;
        }

        if (info is null)
        {
            _logger.Log($"ERROR  {fileName}  No valid UNB segment found");
            Console.Error.WriteLine($"  No valid UNB segment in {fileName}");
            return ProcessResult.Failed;
        }

        string environment = info.IsTest ? "test" : "prod";
        string destDir = Path.Combine(_outputPath, info.Recipient, environment);

        try
        {
            Directory.CreateDirectory(destDir);
            string destFile = UniqueDestination(destDir, fileName);
            File.Move(filePath, destFile);
            _logger.Log($"MOVED  {fileName}  ->  {info.Recipient}/{environment}/{Path.GetFileName(destFile)}");
            Console.WriteLine($"  Moved: {fileName}  ->  {info.Recipient}/{environment}/");
            return ProcessResult.Moved;
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR  {fileName}  Could not move: {ex.Message}");
            Console.Error.WriteLine($"  Error moving {fileName}: {ex.Message}");
            return ProcessResult.Failed;
        }
    }

    // Appends a counter suffix if a file with the same name already exists at the destination.
    private static string UniqueDestination(string directory, string fileName)
    {
        string candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int counter = 1;
        do
        {
            candidate = Path.Combine(directory, $"{nameOnly}_{counter}{ext}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }
}

internal enum ProcessResult { Moved, Skipped, Failed }
