using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 열 순서와 셀 문자열을 보존하는 CSV 표
/// </summary>
public class CsvTable
{
    static readonly char[] QuoteCharacters = { ',', '"', '\r', '\n' };

    public List<string> Headers { get; } = new List<string>();
    public List<List<string>> Rows { get; } = new List<List<string>>();

    static void WriteRow(StringBuilder output, List<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                output.Append(',');

            string value = cells[i];
            bool quote = value.IndexOfAny(QuoteCharacters) >= 0;

            // 한 열짜리 빈 셀과 무시 가능한 빈 줄 구분
            if (cells.Count == 1 && value.Length == 0)
                quote = true;

            if (quote)
            {
                output.Append('"');
                output.Append(value.Replace("\"", "\"\""));
                output.Append('"');
            }
            else
            {
                output.Append(value);
            }
        }

        output.AppendLine();
    }

    /// <summary>
    /// CSV 원문에서 헤더와 데이터 행 읽기
    /// </summary>
    /// <param name="text">CSV 전체 내용</param>
    /// <param name="source">오류 메시지에 표시할 원본 이름</param>
    /// <returns>열 순서와 셀 값을 보존한 표</returns>
    public static CsvTable Parse(string text, string source = "CSV")
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("원본 이름 필요", nameof(source));

        CsvTable table = new CsvTable();
        List<string> cells = new List<string>();
        StringBuilder field = new StringBuilder();

        bool quoted = false;
        bool closedQuote = false;
        bool touched = false;
        int line = 1;
        int recordLine = 1;

        void FinishRecord()
        {
            if (!touched) return;

            cells.Add(field.ToString());

            if (table.Headers.Count == 0)
            {
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

                foreach (string header in cells)
                {
                    if (string.IsNullOrWhiteSpace(header) || header != header.Trim())
                        throw new FormatException($"{source}:{recordLine}: 비어 있거나 앞뒤 공백이 있는 헤더");

                    if (!names.Add(header))
                        throw new FormatException($"{source}:{recordLine}: 중복 헤더 {header}");
                }

                table.Headers.AddRange(cells);
            }
            else
            {
                if (cells.Count != table.Headers.Count)
                    throw new FormatException($"{source}:{recordLine}: 헤더는 {table.Headers.Count}열, 현재 행은 {cells.Count}열");

                table.Rows.Add(new List<string>(cells));
            }

            cells.Clear();
            field.Clear();
            closedQuote = false;
            touched = false;
        }

        int start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;

        for (int i = start; i < text.Length; i++)
        {
            char ch = text[i];
            bool newLine = ch == '\r' || ch == '\n';

            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                }
                else
                {
                    field.Append(ch);

                    if (newLine)
                    {
                        if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            field.Append('\n');
                            i++;
                        }

                        line++;
                    }
                }

                continue;
            }

            if (closedQuote && ch != ',' && !newLine)
                throw new FormatException($"{source}:{line}: 닫는 따옴표 뒤에는 쉼표 또는 줄바꿈 필요");

            if (newLine)
            {
                FinishRecord();

                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                line++;
                recordLine = line;
                continue;
            }

            touched = true;

            if (ch == ',')
            {
                cells.Add(field.ToString());
                field.Clear();
                closedQuote = false;
            }
            else if (ch == '"')
            {
                if (field.Length != 0)
                    throw new FormatException($"{source}:{line}: 따옴표는 셀의 처음에만 사용 가능");

                quoted = true;
            }
            else
            {
                field.Append(ch);
            }
        }

        if (quoted)
            throw new FormatException($"{source}:{recordLine}: 닫히지 않은 따옴표");

        FinishRecord();

        if (table.Headers.Count == 0)
            throw new FormatException($"{source}: CSV 헤더 없음");

        return table;
    }

    /// <summary>
    /// 현재 표를 CSV 원문으로 변환
    /// </summary>
    /// <returns>헤더와 데이터 행을 포함한 CSV 문자열</returns>
    public string ToCsv()
    {
        StringBuilder output = new StringBuilder();
        WriteRow(output, Headers);

        foreach (List<string> row in Rows)
        {
            if (row.Count != Headers.Count)
                throw new InvalidOperationException("저장할 행의 열 수와 헤더 수 불일치");

            WriteRow(output, row);
        }

        return output.ToString();
    }
}