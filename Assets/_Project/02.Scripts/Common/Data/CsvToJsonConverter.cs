using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// CSV 헤더와 행을 JSON으로 변환
/// </summary>
public static class CsvToJsonConverter
{
    /// <summary>
    /// CSV의 모든 셀을 문자열로 보존하며 JSON 생성
    /// </summary>
    /// <param name="text">CSV 전체 내용</param>
    /// <param name="source">오류 표시에 사용할 원본 이름</param>
    /// <returns>Rows 배열을 포함한 JSON 문자열</returns>
    public static string Convert(string text, string source = "CSV")
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("원본 이름 필요", nameof(source));

        JArray rows = new JArray();
        List<string> fields = new List<string>();
        StringBuilder field = new StringBuilder();

        string[] headers = null;

        bool quoted = false;
        bool closedQuote = false;
        bool recordTouched = false;

        int line = 1;
        int recordLine = 1;
        int start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;

        void FinishRecord()
        {
            // 셀이 없는 완전히 빈 행만 제외
            if (!recordTouched && fields.Count == 0 && field.Length == 0)
                return;

            fields.Add(field.ToString());

            if (headers == null)
            {
                headers = fields.ToArray();

                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

                foreach (string header in headers)
                {
                    if (string.IsNullOrWhiteSpace(header) || header != header.Trim())
                        throw new FormatException($"{source}:{recordLine}: " + "헤더의 빈 값 또는 앞뒤 공백 사용 불가");

                    if (!names.Add(header))
                        throw new FormatException($"{source}:{recordLine}: 중복 헤더: {header}");
                }
            }
            else
            {
                if (fields.Count != headers.Length)
                    throw new FormatException($"{source}:{recordLine}: 열 수 불일치 " + $"(헤더 {headers.Length}개, 현재 행 {fields.Count}개)");

                JObject row = new JObject();

                for (int i = 0; i < headers.Length; i++)
                {
                    row[headers[i]] = fields[i];
                }

                rows.Add(row);
            }

            fields.Clear();
            field.Clear();
            closedQuote = false;
            recordTouched = false;
        }

        for (int i = start; i < text.Length; i++)
        {
            char character = text[i];
            bool isNewLine = character == '\r' || character == '\n';

            if (quoted)
            {
                if (character == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        // CSV의 이중 따옴표 이스케이프
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
                    field.Append(character);

                    if (isNewLine)
                    {
                        // 셀 내부 줄바꿈 원문 보존
                        if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            field.Append('\n');
                            i++;
                        }

                        line++;
                    }
                }

                continue;
            }

            if (closedQuote && character != ',' && !isNewLine)
                throw new FormatException($"{source}:{line}: " + "닫는 따옴표 뒤에는 쉼표 또는 줄바꿈 필요");

            if (character == '"')
            {
                if (field.Length > 0)
                    throw new FormatException($"{source}:{line}: " + "따옴표는 셀의 처음에만 사용 가능");

                quoted = true;
                recordTouched = true;
            }
            else if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();

                closedQuote = false;
                recordTouched = true;
            }
            else if (isNewLine)
            {
                FinishRecord();

                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                line++;
                recordLine = line;
            }
            else
            {
                field.Append(character);
                recordTouched = true;
            }
        }

        if (quoted)
            throw new FormatException($"{source}:{recordLine}: 닫히지 않은 따옴표");

        FinishRecord();

        if (headers == null)
            throw new FormatException($"{source}: CSV 헤더 없음");

        JObject document = new JObject
        {
            ["Rows"] = rows
        };

        return document.ToString(Formatting.Indented);
    }
}