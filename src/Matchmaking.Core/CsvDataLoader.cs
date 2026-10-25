namespace Matchmaking.Core;

public static class CsvDataLoader
{
    public static Result<IReadOnlyList<MatchRecord>> Load(string path)
    {
        try
        {
            var parsed = MatchParser.ParseCsv(File.ReadAllLines(path));
            if (!parsed.IsValid)
                return Result<IReadOnlyList<MatchRecord>>.Fail("MALFORMED_CSV",
                    $"Invalid CSV '{path}': {string.Join("; ", parsed.Errors)}");
            return Result<IReadOnlyList<MatchRecord>>.Ok(parsed.Matches);
        }
        catch (FileNotFoundException ex)
        {
            return Result<IReadOnlyList<MatchRecord>>.Fail("FILE_NOT_FOUND", $"Cannot load '{path}': {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            return Result<IReadOnlyList<MatchRecord>>.Fail("FILE_NOT_FOUND", $"Cannot load '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<IReadOnlyList<MatchRecord>>.Fail("FILE_ACCESS_DENIED", $"Cannot load '{path}': {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<IReadOnlyList<MatchRecord>>.Fail("FILE_IO_ERROR", $"Cannot load '{path}': {ex.Message}");
        }
    }
}
