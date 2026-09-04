namespace EDIRouter;

// splitQualifier is the NAD party qualifier to route by ("SU", "BY", ...),
// or null to route on the UNB recipient alone.
public sealed class FileProcessor(string inputPath, string outputPath, string? splitQualifier, Logger logger)
{
    private readonly string _inputPath = inputPath;
    private readonly string _outputPath = outputPath;
    private readonly string? _splitQualifier = splitQualifier;
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

        if (_splitQualifier is { } qualifier)
            return ProcessFileSplit(filePath, fileName, qualifier);

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

    // --split mode: route into a per-GLN subdirectory and, when a file contains
    // messages for more than one NAD+<qualifier> GLN, emit one interchange per
    // GLN with a corrected UNZ message count.
    private ProcessResult ProcessFileSplit(string filePath, string fileName, string nadQualifier)
    {
        EdiInterchange? interchange;

        try
        {
            interchange = EdiParser.ParseInterchange(filePath, nadQualifier);
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

        if (interchange is null)
        {
            _logger.Log($"ERROR  {fileName}  No valid EDIFACT interchange found");
            Console.Error.WriteLine($"  No valid EDIFACT interchange in {fileName}");
            return ProcessResult.Failed;
        }

        string environment = interchange.IsTest ? "test" : "prod";
        var groups = interchange.GroupByGln();

        try
        {
            // Single GLN: move the file unchanged into its GLN subdirectory.
            if (groups.Count == 1)
            {
                var group = groups[0];
                string destDir = Path.Combine(_outputPath, interchange.Recipient, environment, group.Gln);
                Directory.CreateDirectory(destDir);
                string destFile = UniqueDestination(destDir, fileName);
                File.Move(filePath, destFile);

                string rel = $"{interchange.Recipient}/{environment}/{group.Gln}/{Path.GetFileName(destFile)}";
                _logger.Log($"MOVED  {fileName}  ->  {rel}");
                Console.WriteLine($"  Moved: {fileName}  ->  {interchange.Recipient}/{environment}/{group.Gln}/");
                return ProcessResult.Moved;
            }

            // Multiple GLNs: write one interchange per GLN, then remove the original.
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int index = 1;

            foreach (var group in groups)
            {
                string destDir = Path.Combine(_outputPath, interchange.Recipient, environment, group.Gln);
                Directory.CreateDirectory(destDir);
                string destFile = UniqueDestination(destDir, $"{nameOnly}_{index}{ext}");
                File.WriteAllText(destFile, group.Content, interchange.Encoding);

                string rel = $"{interchange.Recipient}/{environment}/{group.Gln}/{Path.GetFileName(destFile)}";
                _logger.Log($"SPLIT  {fileName}  ->  {rel}  ({group.MessageCount} msg)");
                Console.WriteLine($"  Split: {fileName}  ->  {interchange.Recipient}/{environment}/{group.Gln}/  ({group.MessageCount} msg)");
                index++;
            }

            File.Delete(filePath);
            _logger.Log($"SPLIT  {fileName}  into {groups.Count} files by GLN");
            return ProcessResult.Moved;
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR  {fileName}  Could not split/move: {ex.Message}");
            Console.Error.WriteLine($"  Error processing {fileName}: {ex.Message}");
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
