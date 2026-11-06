using System.Globalization;
using System.Xml.Linq;
using BootstrapBlazor.OnlyServer1.Models.Tools;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace BootstrapBlazor.OnlyServer1.Services.Tools;

/// <summary>
/// Reads mass values from a weight list and copies edited values into an OCC workbook.
/// </summary>
public sealed class OCCMassService(IHostEnvironment environment, ILogger<OCCMassService> logger)
{
    private static readonly int[] WeightRows =
    [
        17, 18, 23, 24, 29, 30, 35, 36, 40, 41, 47, 48, 60, 66, 69, 70,
        71, 72, 78, 79, 88, 89, 92, 94, 97, 104, 105, 106, 107, 108, 116
    ];

    private const long MaxFileSize = 100 * 1024 * 1024;

    public Task<IReadOnlyList<OCCMassWeightItem>> ReadWeightsAsync(
        byte[] weightListContent,
        string weightListFileName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            ValidateFile(weightListContent, weightListFileName, "重量表");
            using var stream = new MemoryStream(weightListContent, writable: false);
            using var workbook = CreateWorkbook(stream, weightListFileName);
            cancellationToken.ThrowIfCancellationRequested();
            return (IReadOnlyList<OCCMassWeightItem>)ReadWeightValuesAsItems(workbook);
        }, cancellationToken);
    }

    public Task<OCCMassProcessResult> ProcessAsync(
        byte[] occContent,
        string occFileName,
        IReadOnlyList<OCCMassWeightItem> weights,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Process(
            occContent,
            occFileName,
            weights,
            cancellationToken), cancellationToken);
    }

    private OCCMassProcessResult Process(
        byte[] occContent,
        string occFileName,
        IReadOnlyList<OCCMassWeightItem> weights,
        CancellationToken cancellationToken)
    {
        ValidateFile(occContent, occFileName, "OCC");
        ValidateWeights(weights);

        using var occStream = new MemoryStream(occContent, writable: false);
        using var occWorkbook = CreateWorkbook(occStream, occFileName);

        cancellationToken.ThrowIfCancellationRequested();

        var version = FindVersion(occWorkbook)
                     ?? throw new InvalidOperationException("OCC 文件的 Versions 页未找到版本号。");
        var mappings = LoadMappings(version);
        if (mappings.Count != WeightRows.Length)
        {
            throw new InvalidOperationException(
                $"版本 {version} 配置了 {mappings.Count} 个项目，但重量表需要 {WeightRows.Length} 个数据。");
        }

        var targetRows = ResolveTargetRows(occWorkbook, mappings);
        UpdateDesignInput(occWorkbook, targetRows, weights, mappings);

        using var output = new MemoryStream();
        occWorkbook.Write(output, leaveOpen: true);
        var extension = Path.GetExtension(occFileName).ToLowerInvariant();
        var fileName = $"{Path.GetFileNameWithoutExtension(occFileName)}-已复制重量{extension}";

        logger.LogInformation(
            "OCC 重量复制完成: Version={Version}, File={FileName}, UpdatedRows={UpdatedRows}",
            version,
            occFileName,
            targetRows.Count);

        return new OCCMassProcessResult(
            output.ToArray(),
            fileName,
            GetContentType(extension),
            version,
            targetRows.Count);
    }

    private static void ValidateFile(byte[] content, string fileName, string description)
    {
        if (content.Length == 0)
        {
            throw new InvalidOperationException($"{description}文件不能为空。");
        }

        if (content.Length > MaxFileSize)
        {
            throw new InvalidOperationException($"{description}文件不能超过 100 MB。");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".xls" and not ".xlsx")
        {
            throw new InvalidOperationException($"{description}文件必须是 .xls 或 .xlsx 格式。");
        }
    }

    private static IWorkbook CreateWorkbook(Stream stream, string fileName)
    {
        return Path.GetExtension(fileName).Equals(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);
    }

    private static string? FindVersion(IWorkbook workbook)
    {
        var sheet = workbook.GetSheet("Versions")
                    ?? throw new InvalidOperationException("OCC 文件缺少 Versions 页。");
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();

        for (var rowIndex = sheet.LastRowNum; rowIndex >= 0; rowIndex--)
        {
            var value = GetCellText(sheet.GetRow(rowIndex)?.GetCell(0), formatter, evaluator);
            if (value.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private List<OCCMassMapping> LoadMappings(string version)
    {
        var configurationPath = Path.Combine(environment.ContentRootPath, "OCCMassMapping.xml");
        if (!File.Exists(configurationPath))
        {
            throw new InvalidOperationException($"未找到 OCC 项目映射文件: {configurationPath}");
        }

        var document = XDocument.Load(configurationPath);
        var versionElement = document.Root?
            .Elements("Version")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("version"),
                version,
                StringComparison.OrdinalIgnoreCase));

        var mappingElements = versionElement?.Elements("Item").ToList() ?? [];
        if (mappingElements.Count == 0)
        {
            mappingElements = document.Root?
                .Elements("Version")
                .FirstOrDefault(element => string.Equals(
                    (string?)element.Attribute("version"),
                    "Default",
                    StringComparison.OrdinalIgnoreCase))
                ?.Elements("Item")
                .ToList() ?? [];
        }

        if (mappingElements.Count == 0)
        {
            throw new InvalidOperationException($"OCCMassMapping.xml 未配置版本 {version}，且没有 Default 映射。");
        }

        var mappings = mappingElements
            .Select(item => new OCCMassMapping(
                ((string?)item.Attribute("weightCode") ?? string.Empty).Trim(),
                ((string?)item.Attribute("occCode") ?? string.Empty).Trim(),
                int.TryParse((string?)item.Attribute("targetRow"), out var row) ? row : 0))
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.WeightCode)
                              && !string.IsNullOrWhiteSpace(mapping.OccCode))
            .ToList();

        if (mappings.Count == 0)
        {
            throw new InvalidOperationException($"版本 {version} 的项目映射为空。");
        }

        return mappings;
    }

    private static List<double> ReadWeightValues(IWorkbook workbook)
    {
        var sheet = workbook.GetSheet("DI")
                    ?? throw new InvalidOperationException("重量表缺少 DI 页。");
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        var values = new List<double>(WeightRows.Length);

        foreach (var rowNumber in WeightRows)
        {
            var cell = sheet.GetRow(rowNumber - 1)?.GetCell(2);
            var text = GetCellText(cell, formatter, evaluator);
            if (string.IsNullOrWhiteSpace(text))
            {
                values.Add(0);
                continue;
            }

            if (!TryParseDouble(text, out var value))
            {
                throw new FormatException($"重量表 DI 页第 {rowNumber} 行的 C 列无法转换为数字: {text}");
            }

            values.Add(value);
        }

        return values;
    }

    private static List<OCCMassWeightItem> ReadWeightValuesAsItems(IWorkbook workbook)
    {
        var values = ReadWeightValues(workbook);
        var sheet = workbook.GetSheet("DI")
                    ?? throw new InvalidOperationException("重量表缺少 DI 页。");
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();

        return WeightRows
            .Select((sourceRow, index) => new OCCMassWeightItem
            {
                SourceRow = sourceRow,
                Code = GetCellText(sheet.GetRow(sourceRow - 1)?.GetCell(0), formatter, evaluator),
                Description = GetCellText(sheet.GetRow(sourceRow - 1)?.GetCell(1), formatter, evaluator),
                Value = values[index]
            })
            .ToList();
    }

    private static void ValidateWeights(IReadOnlyList<OCCMassWeightItem> weights)
    {
        if (weights.Count != WeightRows.Length)
        {
            throw new InvalidOperationException($"重量数据数量应为 {WeightRows.Length}，实际为 {weights.Count}。");
        }

        for (var index = 0; index < weights.Count; index++)
        {
            if (!double.IsFinite(weights[index].Value))
            {
                throw new FormatException($"第 {index + 1} 个重量值不是有效数字。");
            }
        }
    }

    private static List<int> ResolveTargetRows(
        IWorkbook workbook,
        IReadOnlyList<OCCMassMapping> mappings)
    {
        var sheet = workbook.GetSheet("Design-Input")
                    ?? throw new InvalidOperationException("OCC 文件缺少 Design-Input 页。");
        var formatter = new DataFormatter(CultureInfo.InvariantCulture);
        var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        var rowsByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var code = GetCellText(sheet.GetRow(rowIndex)?.GetCell(0), formatter, evaluator);
            if (!string.IsNullOrWhiteSpace(code) && !rowsByCode.ContainsKey(code))
            {
                rowsByCode[code] = rowIndex + 1;
            }
        }

        var missingCodes = mappings
            .Where(mapping => !rowsByCode.ContainsKey(mapping.OccCode))
            .Select(mapping => mapping.OccCode)
            .ToList();
        if (missingCodes.Count > 0)
        {
            throw new InvalidOperationException(
                $"OCC 的 Design-Input 页未找到项目编码: {string.Join(", ", missingCodes)}");
        }

        return mappings.Select(mapping => rowsByCode[mapping.OccCode]).ToList();
    }

    private static void UpdateDesignInput(
        IWorkbook workbook,
        IReadOnlyList<int> rows,
        IReadOnlyList<OCCMassWeightItem> weights,
        IReadOnlyList<OCCMassMapping> mappings)
    {
        var sheet = workbook.GetSheet("Design-Input")
                    ?? throw new InvalidOperationException("OCC 文件缺少 Design-Input 页。");

        var weightsByCode = weights
            .ToDictionary(weight => weight.Code, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < mappings.Count; index++)
        {
            var row = sheet.GetRow(rows[index] - 1) ?? sheet.CreateRow(rows[index] - 1);
            var cell = row.GetCell(2) ?? row.CreateCell(2);
            cell.SetCellValue(weightsByCode[mappings[index].WeightCode].Value);
        }
    }

    private static string GetCellText(ICell? cell, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        return cell is null ? string.Empty : formatter.FormatCellValue(cell, evaluator).Trim();
    }

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture, out value);
    }

    private static string GetContentType(string extension)
    {
        return extension == ".xls"
            ? "application/vnd.ms-excel"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }
}
