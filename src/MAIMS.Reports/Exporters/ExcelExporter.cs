using ClosedXML.Excel;

namespace MAIMS.Reports.Exporters;

/// <summary>
/// Excel export helper using ClosedXML. Produces an .xlsx from any flat DTO list.
/// </summary>
public static class ExcelExporter
{
    public static byte[] ToExcel<T>(IEnumerable<T> rows, string sheetName = "Report")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        ws.Cell(1, 1).InsertTable(rows);
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
