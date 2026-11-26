using System.Text;

namespace ReqPipeline.Core.Testing;

/// <summary>
/// TestSpecificationDocument から RFC 4180 準拠・UTF-8 BOM 付きの CSV を生成するエクスポーター。
/// 基本 13 列（固定順）＋任意の実行記録用 4 列（Result, Tester, ExecutedAt, Comment）をサポートする。
/// </summary>
public static class CsvTestExporter
{
    private static readonly string[] BaseHeaders =
    [
        "TestCaseId",
        "TestLevel",
        "Technique",
        "RationaleId",
        "CompositeId",
        "SourceRef",
        "ViewpointTitle",
        "Title",
        "Given",
        "When",
        "Then",
        "ExpectedOutcome",
        "ReviewNote"
    ];

    private static readonly string[] ExecutionHeaders =
    [
        "Result",
        "Tester",
        "ExecutedAt",
        "Comment"
    ];

    public static string Export(
        TestSpecificationDocument spec,
        bool includeExecutionColumns = false,
        TestExportLanguage language = TestExportLanguage.Japanese)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var isJa = language == TestExportLanguage.Japanese;
        var sb = new StringBuilder();

        var headers = includeExecutionColumns
            ? BaseHeaders.Concat(ExecutionHeaders).ToArray()
            : BaseHeaders;

        AppendCsvRow(sb, headers);

        foreach (var tc in spec.AllTestCases)
        {
            var givenText = FormatGiven(tc.GivenConditions, isJa);
            var whenText = FormatWhen(tc.WhenSteps, isJa);
            var thenText = isJa ? tc.ThenTextJa : tc.ThenTextEn;
            var reviewNote = isJa ? (tc.ReviewNoteJa ?? string.Empty) : (tc.ReviewNoteEn ?? string.Empty);

            var row = new List<string>(headers.Length)
            {
                tc.TestCaseId,
                tc.TestLevel.ToString(),
                tc.Technique.ToString(),
                tc.RationaleId ?? string.Empty,
                tc.CompositeId ?? string.Empty,
                tc.SourceRef,
                isJa ? tc.ViewpointTitle : tc.ViewpointTitleEn,
                isJa ? tc.Title : tc.TitleEn,
                givenText,
                whenText,
                thenText,
                tc.ExpectedOutcome.ToString(),
                reviewNote
            };

            if (includeExecutionColumns)
            {
                row.Add(string.Empty);
                row.Add(string.Empty);
                row.Add(string.Empty);
                row.Add(string.Empty);
            }

            AppendCsvRow(sb, row);
        }

        return sb.ToString();
    }

    public static byte[] ExportUtf8BomBytes(
        TestSpecificationDocument spec,
        bool includeExecutionColumns = false,
        TestExportLanguage language = TestExportLanguage.Japanese)
    {
        var csv = Export(spec, includeExecutionColumns, language);
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv);
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string FormatGiven(IReadOnlyList<TestConditionEntry> conditions, bool isJa)
    {
        if (conditions.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n", conditions.Select(c =>
            isJa ? $"{c.LabelJa} = {c.ValueLabelJa}" : $"{c.LabelEn} = {c.ValueLabelEn}"));
    }

    private static string FormatWhen(IReadOnlyList<TestActionEntry> steps, bool isJa)
    {
        if (steps.Count == 0)
        {
            return string.Empty;
        }

        if (steps.Count == 1)
        {
            return isJa ? steps[0].ActionTextJa : steps[0].ActionTextEn;
        }

        return string.Join("\n", steps.Select(s =>
            $"{s.StepNumber}. {(isJa ? s.ActionTextJa : s.ActionTextEn)}"));
    }

    private static void AppendCsvRow(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(EscapeCsvField(fields[i]));
        }
        sb.Append("\r\n");
    }

    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
