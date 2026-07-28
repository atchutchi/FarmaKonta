using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Inventory.Import;

namespace Nofarma.Infrastructure.Import;

public static class CsvInventoryFileReader
{
    public static async Task<InventoryFileReadResult> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string hash = await CalculateHashAsync(filePath, cancellationToken);
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: false);

            CsvRecord? headerRecord = await ReadRecordAsync(reader, 1, cancellationToken);
            if (headerRecord is null)
            {
                throw InvalidContent("O ficheiro CSV está vazio.");
            }

            char delimiter = DetectDelimiter(headerRecord.Value.Text);
            IReadOnlyList<string?> rawHeaders = ParseRecord(headerRecord.Value.Text, delimiter);
            string[] headers = rawHeaders.Select(value => value?.Trim() ?? string.Empty).ToArray();
            if (headers.Length == 0 || headers.All(string.IsNullOrWhiteSpace))
            {
                throw InvalidContent("O ficheiro CSV não contém cabeçalhos válidos.");
            }

            var rows = new List<InventoryFileRow>();
            int physicalLine = headerRecord.Value.EndLine + 1;
            while (true)
            {
                CsvRecord? record = await ReadRecordAsync(reader, physicalLine, cancellationToken);
                if (record is null)
                {
                    break;
                }

                physicalLine = record.Value.EndLine + 1;
                if (string.IsNullOrWhiteSpace(record.Value.Text))
                {
                    continue;
                }

                if (rows.Count == InventoryFileReader.MaximumDataRows)
                {
                    throw new InventoryFileException(
                        "inventory_row_limit_exceeded",
                        "O ficheiro excede o limite de 50 000 linhas de dados.");
                }

                IReadOnlyList<string?> cells = ParseRecord(record.Value.Text, delimiter);
                rows.Add(new InventoryFileRow(record.Value.StartLine, cells));
            }

            return new InventoryFileReadResult(
                Path.GetFileName(filePath),
                hash,
                WorksheetName: null,
                headers,
                rows);
        }
        catch (InventoryFileException)
        {
            throw;
        }
        catch (DecoderFallbackException exception)
        {
            throw new InventoryFileException(
                "inventory_csv_encoding_invalid",
                "O ficheiro CSV deve usar codificação UTF-8.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InventoryFileException(
                "inventory_file_unavailable",
                "Não foi possível ler o ficheiro seleccionado.",
                exception);
        }
    }

    private static async Task<string> CalculateHashAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static char DetectDelimiter(string header)
    {
        int commas = CountDelimiter(header, ',');
        int semicolons = CountDelimiter(header, ';');
        if (commas == 0 && semicolons == 0)
        {
            throw InvalidContent("O separador CSV não foi reconhecido.");
        }

        return semicolons > commas ? ';' : ',';
    }

    private static int CountDelimiter(string value, char delimiter)
    {
        int count = 0;
        bool quoted = false;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                if (quoted && index + 1 < value.Length && value[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && value[index] == delimiter)
            {
                count++;
            }
        }

        return count;
    }

    private static List<string?> ParseRecord(string record, char delimiter)
    {
        var values = new List<string?>();
        var value = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < record.Length; index++)
        {
            char character = record[index];
            if (character == '"')
            {
                if (quoted && index + 1 < record.Length && record[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == delimiter && !quoted)
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        if (quoted)
        {
            throw InvalidContent("O ficheiro CSV contém aspas não terminadas.");
        }

        values.Add(value.ToString());
        return values;
    }

    private static async Task<CsvRecord?> ReadRecordAsync(
        StreamReader reader,
        int startLine,
        CancellationToken cancellationToken)
    {
        string? line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            return null;
        }

        var record = new StringBuilder(line);
        int endLine = startLine;
        while (HasOpenQuote(record))
        {
            line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw InvalidContent("O ficheiro CSV contém aspas não terminadas.");
            }

            record.Append('\n').Append(line);
            endLine++;
        }

        return new CsvRecord(startLine, endLine, record.ToString());
    }

    private static bool HasOpenQuote(StringBuilder record)
    {
        bool quoted = false;
        for (int index = 0; index < record.Length; index++)
        {
            if (record[index] != '"')
            {
                continue;
            }

            if (quoted && index + 1 < record.Length && record[index + 1] == '"')
            {
                index++;
            }
            else
            {
                quoted = !quoted;
            }
        }

        return quoted;
    }

    private static InventoryFileException InvalidContent(string message) => new(
        "inventory_csv_invalid",
        message);

    private readonly record struct CsvRecord(int StartLine, int EndLine, string Text);
}
