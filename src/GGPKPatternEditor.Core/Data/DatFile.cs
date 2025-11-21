using System.Data;
using System.Text;

namespace GGPKPatternEditor.Core.Data;

/// <summary>
/// Parser for POE .dat/.dat64 files
/// </summary>
public class DatFile
{
    public string TableName { get; private set; } = string.Empty;
    public int RowCount { get; private set; }
    public int RowWidth { get; private set; }
    public bool Is64Bit { get; private set; }
    public byte[] RawData { get; private set; } = Array.Empty<byte>();
    public byte[] VariableData { get; private set; } = Array.Empty<byte>();
    public long DataSectionOffset { get; private set; }
    public long VariableSectionOffset { get; private set; }
    public bool HasSchema { get; private set; }

    public DataTable? DataTable { get; private set; }

    /// <summary>
    /// Parses a .dat or .dat64 file
    /// </summary>
    public static DatFile Parse(byte[] data, string fileName)
    {
        var datFile = new DatFile();
        datFile.TableName = Path.GetFileNameWithoutExtension(fileName);
        datFile.Is64Bit = fileName.EndsWith("64", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains(".dat64", StringComparison.OrdinalIgnoreCase);

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Read row count (first 4 bytes)
        datFile.RowCount = reader.ReadInt32();

        if (datFile.RowCount <= 0 || datFile.RowCount > 1000000)
        {
            datFile.RawData = data;
            datFile.BuildFallbackTable(data);
            return datFile;
        }

        // Find the magic marker 0xBBBBBBBB that separates fixed data from variable data
        long magicOffset = FindMagicMarker(data);

        if (magicOffset == -1)
        {
            datFile.DataSectionOffset = 4;
            datFile.RawData = data;
            datFile.RowWidth = datFile.RowCount > 0 ? (data.Length - 4) / datFile.RowCount : 0;
        }
        else
        {
            datFile.DataSectionOffset = 4;
            datFile.VariableSectionOffset = magicOffset;
            datFile.RowWidth = datFile.RowCount > 0 ? (int)(magicOffset - 4) / datFile.RowCount : 0;

            int dataLength = (int)(magicOffset - 4);
            datFile.RawData = new byte[dataLength];
            Array.Copy(data, 4, datFile.RawData, 0, dataLength);

            int varLength = data.Length - (int)magicOffset;
            datFile.VariableData = new byte[varLength];
            Array.Copy(data, magicOffset, datFile.VariableData, 0, varLength);
        }

        // Build DataTable using schema if available
        datFile.BuildDataTable(data);

        return datFile;
    }

    private static long FindMagicMarker(byte[] data)
    {
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
        // Try to get schema for this table
        var schema = DatSchema.GetTableDefinition(TableName);

        if (schema != null && schema.Count > 0)
        {
            HasSchema = true;
            BuildSchemaBasedTable(fullData, schema);
        }
        else
        {
            HasSchema = false;
            BuildGenericTable(fullData);
        }
    }

    private void BuildSchemaBasedTable(byte[] fullData, Dictionary<string, string> schema)
    {
        DataTable = new DataTable();
        DataTable.Columns.Add("Row", typeof(int));

        // Add columns based on schema
        foreach (var col in schema)
        {
            Type colType = GetColumnType(col.Value);
            DataTable.Columns.Add(col.Key, colType);
        }

        int pointerSize = Is64Bit ? 8 : 4;
        int maxRows = Math.Min(RowCount, 10000);

        for (int row = 0; row < maxRows; row++)
        {
            var rowData = new List<object?> { row };
            int offset = 4 + (row * RowWidth);

            foreach (var col in schema)
            {
                object? value = ReadValue(fullData, ref offset, col.Value, pointerSize);
                rowData.Add(value);
            }

            DataTable.Rows.Add(rowData.ToArray());
        }

        if (RowCount > maxRows)
        {
            var truncRow = new object?[DataTable.Columns.Count];
            truncRow[0] = -1;
            for (int i = 1; i < truncRow.Length; i++)
                truncRow[i] = $"... ({RowCount - maxRows} more rows)";
            DataTable.Rows.Add(truncRow);
        }
    }

    private void BuildGenericTable(byte[] fullData)
    {
        DataTable = new DataTable();

        if (RowCount <= 0 || RowWidth <= 0)
        {
            DataTable.Columns.Add("Raw Data", typeof(string));
            DataTable.Rows.Add(BitConverter.ToString(fullData.Take(Math.Min(1000, fullData.Length)).ToArray()));
            return;
        }

        DataTable.Columns.Add("Row", typeof(int));

        // Create columns based on row width, assuming 4-byte integers
        int numCols = RowWidth / 4;
        int remainder = RowWidth % 4;

        for (int i = 0; i < numCols; i++)
        {
            DataTable.Columns.Add($"Field{i}", typeof(int));
        }
        if (remainder > 0)
        {
            DataTable.Columns.Add($"Field{numCols}_Bytes", typeof(string));
        }

        int maxRows = Math.Min(RowCount, 10000);
        for (int row = 0; row < maxRows; row++)
        {
            var rowData = new List<object> { row };
            int dataOffset = 4 + (row * RowWidth);

            for (int i = 0; i < numCols; i++)
            {
                int pos = dataOffset + (i * 4);
                if (pos + 4 <= fullData.Length)
                {
                    rowData.Add(BitConverter.ToInt32(fullData, pos));
                }
                else
                {
                    rowData.Add(0);
                }
            }

            if (remainder > 0)
            {
                int pos = dataOffset + (numCols * 4);
                if (pos + remainder <= fullData.Length)
                {
                    rowData.Add(BitConverter.ToString(fullData, pos, remainder));
                }
                else
                {
                    rowData.Add("");
                }
            }

            DataTable.Rows.Add(rowData.ToArray());
        }

        if (RowCount > maxRows)
        {
            var truncRow = new object[DataTable.Columns.Count];
            truncRow[0] = -1;
            for (int i = 1; i < truncRow.Length; i++)
                truncRow[i] = $"... ({RowCount - maxRows} more rows)";
            DataTable.Rows.Add(truncRow);
        }
    }

    private void BuildFallbackTable(byte[] data)
    {
        DataTable = new DataTable();
        DataTable.Columns.Add("Info", typeof(string));
        DataTable.Columns.Add("Value", typeof(string));

        DataTable.Rows.Add("Row Count", RowCount.ToString());
        DataTable.Rows.Add("File Size", $"{data.Length} bytes");
        DataTable.Rows.Add("First 100 bytes", BitConverter.ToString(data.Take(100).ToArray()));
    }

    private Type GetColumnType(string typeStr)
    {
        return typeStr.ToLowerInvariant() switch
        {
            "bool" => typeof(bool),
            "i8" or "u8" => typeof(byte),
            "i16" or "u16" => typeof(short),
            "i32" or "u32" => typeof(int),
            "i64" or "u64" => typeof(long),
            "f32" => typeof(float),
            "f64" => typeof(double),
            "string" or "valuestring" => typeof(string),
            "row" or "foreignrow" => typeof(string), // Show as "Row X" or "null"
            _ when typeStr.StartsWith("array") => typeof(string), // Show arrays as string representation
            _ => typeof(string)
        };
    }

    private object? ReadValue(byte[] data, ref int offset, string typeStr, int pointerSize)
    {
        try
        {
            string type = typeStr.ToLowerInvariant();

            if (type.StartsWith("array"))
            {
                return ReadArray(data, ref offset, typeStr, pointerSize);
            }

            return type switch
            {
                "bool" => ReadBool(data, ref offset),
                "i8" => ReadI8(data, ref offset),
                "u8" => ReadU8(data, ref offset),
                "i16" => ReadI16(data, ref offset),
                "u16" => ReadU16(data, ref offset),
                "i32" => ReadI32(data, ref offset),
                "u32" => ReadU32(data, ref offset),
                "i64" => ReadI64(data, ref offset),
                "u64" => ReadU64(data, ref offset),
                "f32" => ReadF32(data, ref offset),
                "f64" => ReadF64(data, ref offset),
                "string" => ReadString(data, ref offset, pointerSize),
                "valuestring" => ReadString(data, ref offset, pointerSize),
                "row" => ReadRow(data, ref offset, pointerSize),
                "foreignrow" => ReadForeignRow(data, ref offset, pointerSize),
                _ => ReadUnknown(data, ref offset, 4)
            };
        }
        catch
        {
            return "ERROR";
        }
    }

    private bool ReadBool(byte[] data, ref int offset)
    {
        bool value = data[offset] != 0;
        offset += 1;
        return value;
    }

    private sbyte ReadI8(byte[] data, ref int offset)
    {
        sbyte value = (sbyte)data[offset];
        offset += 1;
        return value;
    }

    private byte ReadU8(byte[] data, ref int offset)
    {
        byte value = data[offset];
        offset += 1;
        return value;
    }

    private short ReadI16(byte[] data, ref int offset)
    {
        short value = BitConverter.ToInt16(data, offset);
        offset += 2;
        return value;
    }

    private ushort ReadU16(byte[] data, ref int offset)
    {
        ushort value = BitConverter.ToUInt16(data, offset);
        offset += 2;
        return value;
    }

    private int ReadI32(byte[] data, ref int offset)
    {
        int value = BitConverter.ToInt32(data, offset);
        offset += 4;
        return value;
    }

    private uint ReadU32(byte[] data, ref int offset)
    {
        uint value = BitConverter.ToUInt32(data, offset);
        offset += 4;
        return value;
    }

    private long ReadI64(byte[] data, ref int offset)
    {
        long value = BitConverter.ToInt64(data, offset);
        offset += 8;
        return value;
    }

    private ulong ReadU64(byte[] data, ref int offset)
    {
        ulong value = BitConverter.ToUInt64(data, offset);
        offset += 8;
        return value;
    }

    private float ReadF32(byte[] data, ref int offset)
    {
        float value = BitConverter.ToSingle(data, offset);
        offset += 4;
        return value;
    }

    private double ReadF64(byte[] data, ref int offset)
    {
        double value = BitConverter.ToDouble(data, offset);
        offset += 8;
        return value;
    }

    private string ReadString(byte[] data, ref int offset, int pointerSize)
    {
        long stringOffset;
        if (pointerSize == 8)
        {
            stringOffset = BitConverter.ToInt64(data, offset);
            offset += 8;
        }
        else
        {
            stringOffset = BitConverter.ToInt32(data, offset);
            offset += 4;
        }

        if (stringOffset < 0 || VariableData == null || stringOffset >= VariableData.Length)
            return "";

        // Read UTF-16 string from variable data section (after magic marker)
        int actualOffset = (int)stringOffset + 8; // Skip magic marker
        if (actualOffset >= VariableData.Length) return "";

        var sb = new StringBuilder();
        for (int i = actualOffset; i < VariableData.Length - 1; i += 2)
        {
            char c = (char)(VariableData[i] | (VariableData[i + 1] << 8));
            if (c == '\0') break;
            sb.Append(c);
        }

        return sb.ToString();
    }

    private string ReadRow(byte[] data, ref int offset, int pointerSize)
    {
        long rowIndex;
        if (pointerSize == 8)
        {
            rowIndex = BitConverter.ToInt64(data, offset);
            offset += 8;
        }
        else
        {
            rowIndex = BitConverter.ToInt32(data, offset);
            offset += 4;
        }

        if (rowIndex == -1 || rowIndex == 0xFEFEFEFE || rowIndex == 0xFEFEFEFEFEFEFEFE)
            return "null";

        return $"Row {rowIndex}";
    }

    private string ReadForeignRow(byte[] data, ref int offset, int pointerSize)
    {
        // Foreign row is typically rowid + unknown/tableid
        long rowIndex;
        if (pointerSize == 8)
        {
            rowIndex = BitConverter.ToInt64(data, offset);
            offset += 8;
            offset += 8; // Skip second part
        }
        else
        {
            rowIndex = BitConverter.ToInt32(data, offset);
            offset += 4;
            offset += 4; // Skip second part
        }

        if (rowIndex == -1 || rowIndex == 0xFEFEFEFE || rowIndex == 0xFEFEFEFEFEFEFEFE)
            return "null";

        return $"Row {rowIndex}";
    }

    private string ReadArray(byte[] data, ref int offset, string typeStr, int pointerSize)
    {
        // Array format: count (4/8 bytes) + offset (4/8 bytes)
        long count, arrayOffset;

        if (pointerSize == 8)
        {
            count = BitConverter.ToInt64(data, offset);
            offset += 8;
            arrayOffset = BitConverter.ToInt64(data, offset);
            offset += 8;
        }
        else
        {
            count = BitConverter.ToInt32(data, offset);
            offset += 4;
            arrayOffset = BitConverter.ToInt32(data, offset);
            offset += 4;
        }

        if (count == 0)
            return "[]";

        if (count > 100)
            return $"[{count} items]";

        return $"[{count} items @ {arrayOffset}]";
    }

    private string ReadUnknown(byte[] data, ref int offset, int size)
    {
        string hex = BitConverter.ToString(data, offset, Math.Min(size, data.Length - offset));
        offset += size;
        return hex;
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
