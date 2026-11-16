using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// CSV 헤더와 데이터 행을 JSON으로 변환
/// </summary>
public static class CsvToJsonConverter
{
    /// <summary>
    /// 모든 셀 값을 문자열로 보존한 Rows 형식 JSON 생성
    /// </summary>
    /// <param name="text">CSV 전체 내용</param>
    /// <param name="source">오류 메시지에 표시할 원본 이름</param>
    /// <returns>Rows 배열을 포함한 JSON 문자열</returns>
    public static string Convert(string text, string source = "CSV")
    {
        CsvTable table = CsvTable.Parse(text, source);
        JArray rows = new JArray();

        foreach (List<string> cells in table.Rows)
        {
            JObject row = new JObject();

            for (int i = 0; i < table.Headers.Count; i++)
                row[table.Headers[i]] = cells[i];

            rows.Add(row);
        }

        JObject document = new JObject
        {
            ["Rows"] = rows
        };

        return document.ToString(Formatting.Indented);
    }
}