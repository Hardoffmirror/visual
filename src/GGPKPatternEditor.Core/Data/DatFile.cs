using System.Data;
using System.Text;

namespace GGPKPatternEditor.Core.Data;

/// <summary>
/// Parser for POE .dat/.dat64 files
/// </summary>
public class DatFile
{
    public int RowCount { get; private set; }
    public int RowWidth { get; private set; }
    public bool Is64Bit { get; private set; }
    public byte[] RawData { get; private set; } = Array.Empty<byte>();
    public byte[] VariableData { get; private set; } = Array.Empty<byte>();
    public long DataSectionOffset { get; private set; }
    public long VariableSectionOffset { get; private set; }

    public DataTable? DataTable { get; private set; }

    /// <summary>
    /// Parses a .dat or .dat64 file
    /// </summary>
    public static DatFile Parse(byte[] data, string fileName)
    {
        var datFile = new DatFile();
        datFile.Is64Bit = fileName.EndsWith("64", StringComparison.OrdinalIgnoreCase);

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Read row count (first 4 bytes)
        datFile.RowCount = reader.ReadInt32();

        if (datFile.RowCount <= 0 || datFile.RowCount > 1000000)
        {
            // Invalid or too large, return basic info
            datFile.RawData = data;
            return datFile;
        }

        // Find the magic marker 0xBBBBBBBB that separates fixed data from variable data
        long magicOffset = FindMagicMarker(data);

        if (magicOffset == -1)
        {
            // No variable section
            datFile.DataSectionOffset = 4;
            datFile.RawData = data;
            datFile.RowWidth = datFile.RowCount > 0 ? (data.Length - 4) / datFile.RowCount : 0;
        }
        else
        {
            datFile.DataSectionOffset = 4;
            datFile.VariableSectionOffset = magicOffset;
            datFile.RowWidth = datFile.RowCount > 0 ? (int)(magicOffset - 4) / datFile.RowCount : 0;

            // Extract sections
            int dataLength = (int)(magicOffset - 4);
            datFile.RawData = new byte[dataLength];
            Array.Copy(data, 4, datFile.RawData, 0, dataLength);

            int varLength = data.Length - (int)magicOffset;
            datFile.VariableData = new byte[varLength];
            Array.Copy(data, magicOffset, datFile.VariableData, 0, varLength);
        }

        // Build DataTable for display
        datFile.BuildDataTable(data);

        return datFile;
    }

    private static long FindMagicMarker(byte[] data)
    {
        // Look for 0xBBBBBBBB (8 bytes of 0xBB)
        byte[] magic = { 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB };

        for (int i = 4; i <= data.Length - 8; i++)
        {
            bool found = true;
            for (int j = 0; j < 8; j++)
            {
                if (data[i + j] != 0xBB)
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }

        return -1;
    }

    private void BuildDataTable(byte[] fullData)
    {
        DataTable = new DataTable();

        if (RowCount <= 0 || RowWidth <= 0)
        {
            DataTable.Columns.Add("Raw Data", typeof(string));
            DataTable.Rows.Add(BitConverter.ToString(fullData.Take(Math.Min(1000, fullData.Length)).ToArray()));
            return;
        }

        // Add row index column
        DataTable.Columns.Add("Row", typeof(int));

        // Determine likely column structure based on row width
        int pointerSize = Is64Bit ? 8 : 4;
        int estimatedColumns = Math.Max(1, RowWidth / 4); // Estimate 4 bytes per column

        // Add columns for each potential field
        // We'll show raw hex values and try to interpret common types
        int offset = 0;
        int colIndex = 0;

        while (offset < RowWidth)
        {
            int remaining = RowWidth - offset;

            if (remaining >= 8)
            {
                // Could be a pointer (8 bytes for 64-bit, 4 for 32-bit)
                // Or two int32s, or one int64, etc.
                // For now, show as both Int64 and Hex
                DataTable.Columns.Add($"Col{colIndex}_Int64", typeof(long));
                DataTable.Columns.Add($"Col{colIndex}_Hex", typeof(string));
                offset += 8;
            }
            else if (remaining >= 4)
            {
                DataTable.Columns.Add($"Col{colIndex}_Int32", typeof(int));
                DataTable.Columns.Add($"Col{colIndex}_Hex", typeof(string));
                offset += 4;
            }
            else
            {
                DataTable.Columns.Add($"Col{colIndex}_Bytes", typeof(string));
                offset += remaining;
            }
            colIndex++;
        }

        // Populate rows
        int maxRows = Math.Min(RowCount, 10000); // Limit for performance
        for (int row = 0; row < maxRows; row++)
        {
            var rowData = new List<object> { row };
            offset = 0;

            int dataOffset = 4 + (row * RowWidth);
            if (dataOffset + RowWidth > fullData.Length) break;

            while (offset < RowWidth)
            {
                int remaining = RowWidth - offset;
                int pos = dataOffset + offset;

                if (remaining >= 8)
                {
                    long val = BitConverter.ToInt64(fullData, pos);
                    rowData.Add(val);
                    rowData.Add(BitConverter.ToString(fullData, pos, 8));
                    offset += 8;
                }
                else if (remaining >= 4)
                {
                    int val = BitConverter.ToInt32(fullData, pos);
                    rowData.Add(val);
                    rowData.Add(BitConverter.ToString(fullData, pos, 4));
                    offset += 4;
                }
                else
                {
                    rowData.Add(BitConverter.ToString(fullData, pos, remaining));
                    offset += remaining;
                }
            }

            DataTable.Rows.Add(rowData.ToArray());
        }

        if (RowCount > maxRows)
        {
            // Add indicator that data was truncated
            var truncRow = new object[DataTable.Columns.Count];
            truncRow[0] = -1;
            for (int i = 1; i < truncRow.Length; i++)
                truncRow[i] = $"... ({RowCount - maxRows} more rows)";
            DataTable.Rows.Add(truncRow);
        }
    }

    /// <summary>
    /// Reads a string from the variable data section
    /// </summary>
    public string ReadString(long offset)
    {
        if (VariableData == null || offset < 0 || offset >= VariableData.Length)
            return string.Empty;

        // Skip the magic marker (8 bytes)
        int actualOffset = (int)offset + 8;
        if (actualOffset >= VariableData.Length) return string.Empty;

        // Read null-terminated UTF-16 string
        var sb = new StringBuilder();
        for (int i = actualOffset; i < VariableData.Length - 1; i += 2)
        {
            char c = (char)(VariableData[i] | (VariableData[i + 1] << 8));
            if (c == '\0') break;
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports to CSV format
    /// </summary>
    public string ExportToCsv()
    {
        if (DataTable == null) return string.Empty;

        var sb = new StringBuilder();

        // Header
        sb.AppendLine(string.Join(",", DataTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\"")));

        // Rows
        foreach (DataRow row in DataTable.Rows)
        {
            var values = row.ItemArray.Select(v => $"\"{v?.ToString()?.Replace("\"", "\"\"") ?? ""}\"");
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }
}
