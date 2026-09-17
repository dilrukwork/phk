using System.Text;

namespace AzureResourceLister;

/// <summary>
/// Whole-file, quote-aware CSV tokenizer, shared by every CSV import in the app.
/// Treats a newline as a row separator only when it occurs outside a quoted field —
/// a literal embedded line break inside quotes (e.g. a multi-line description) is kept
/// as part of that field's content instead of incorrectly starting a new row.
/// Splitting a file into lines first (e.g. via File.ReadAllLines) breaks the moment any
/// field contains such a line break, silently corrupting that row and every column after it.
/// </summary>
public static class CsvParser
{
    public static List<List<string>> ParseRows(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"' && i + 1 < n && text[i + 1] == '"') { current.Append('"'); i += 2; continue; }
                if (c == '"') { inQuotes = false; i++; continue; }
                current.Append(c); // includes literal \r or \n while inside quotes
                i++;
                continue;
            }

            if (c == '"') { inQuotes = true; i++; continue; }

            if (c == ',')
            {
                currentRow.Add(current.ToString());
                current.Clear();
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                currentRow.Add(current.ToString());
                current.Clear();
                rows.Add(currentRow);
                currentRow = [];
                i++;
                if (c == '\r' && i < n && text[i] == '\n') i++; // swallow the \n of a \r\n pair
                continue;
            }

            current.Append(c);
            i++;
        }

        // flush a trailing row if the file doesn't end with a newline
        if (current.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(current.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    /// <summary>Builds a case-insensitive header name → column index lookup from the first row.</summary>
    public static Dictionary<string, int> BuildHeaderIndex(List<string> headerRow)
        => headerRow
            .Select((h, i) => (h.Trim().ToLowerInvariant(), i))
            .ToDictionary(t => t.Item1, t => t.i);

    public static string Get(List<string> cols, Dictionary<string, int> headerIndex, string columnName)
    {
        if (!headerIndex.TryGetValue(columnName.ToLowerInvariant(), out var idx)) return string.Empty;
        return idx >= 0 && idx < cols.Count ? cols[idx].Trim() : string.Empty;
    }
}
