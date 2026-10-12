using System.Globalization;
using System.Text;

namespace Outatime;

/// A small Excel (.xlsx) writer: inline strings, a fixed style sheet, Excel tables, a column chart and solid data bars,
/// in an uncompressed zip. Formulas carry cached values for previewers; Excel recalculates them on open.
public static class Xlsx
{
    /// Raw values are indexes into `cellXfs`.
    public enum Style
    {
        Plain, Bold, Date, Time, Hours, Balance, TotalHours, TotalBalance,
        Title, Subtitle, Section, CardLabel, CardHours, CardBank, CardDays, CardCaption,
        Head, HeadRight, Month, RowText, RowHours, RowBalance, RowCount, RowShare, RowBar,
    }

    public abstract record Cell
    {
        public sealed record Text(string Value, Style Style = Style.Plain) : Cell;
        public sealed record Number(double Value, Style Style) : Cell;
        public sealed record Formula(string F, double Cached, Style Style) : Cell;
        /// No value, only formatting (the second half of a merged card).
        public sealed record Blank(Style Style) : Cell;
        public sealed record Empty : Cell;
        public static readonly Cell None = new Empty();
    }

    public sealed record Table(string Name, IReadOnlyList<string> Columns, int Rows, IReadOnlyList<int> Sums);

    public sealed record Series(string Name, string Ref, IReadOnlyList<double> Values, string Color);

    public sealed record Chart((int Col, int Row) From, (int Col, int Row) To, (string Ref, IReadOnlyList<double> Values) Categories,
                               IReadOnlyList<Series> Series);

    public sealed class Sheet(string name)
    {
        public string Name = name;
        public List<List<Cell>> Rows = [];
        public List<double> Widths = [];
        public Dictionary<int, double> Heights = [];
        public List<string> Merges = [];
        public int FrozenRows;
        public bool Gridlines = true;
        public Table? Table;
        public Chart? Chart;
        /// Ranges holding 0…1 shares, drawn as bars without the number.
        public List<string> Bars = [];

        public void Put(int row, int col, Cell cell)
        {
            while (Rows.Count <= row) Rows.Add([]);
            while (Rows[row].Count <= col) Rows[row].Add(Cell.None);
            Rows[row][col] = cell;
        }
    }

    /// A sheet that is one Excel table: header row, `rows`, and with `sums` a totals row. Pasting rows right under
    /// it grows the table, which is how a month report joins the master workbook.
    public static Sheet TableSheet(string name, string table, IReadOnlyList<string> columns, List<List<Cell>> rows,
                                   IReadOnlyList<(int Column, double Total, Style Style)>? sums, IEnumerable<double> widths)
    {
        sums ??= [];
        var s = new Sheet(name) { Widths = widths.ToList(), FrozenRows = 1 };
        s.Rows.Add(columns.Select(c => (Cell)new Cell.Text(c)).ToList());
        if (rows.Count == 0) s.Rows.Add([]); else s.Rows.AddRange(rows);
        if (sums.Count > 0)
        {
            var total = Enumerable.Repeat(Cell.None, columns.Count).ToList();
            total[0] = new Cell.Text("Total", Style.Bold);
            foreach (var c in sums) total[c.Column] = new Cell.Formula($"SUBTOTAL(109,{table}[{columns[c.Column]}])", c.Total, c.Style);
            s.Rows.Add(total);
        }
        s.Table = new Table(table, columns, Math.Max(rows.Count, 1), sums.Select(x => x.Column).ToList());
        return s;
    }

    /// Excel's day number for a moment in the local time zone: days since 1899-12-30, the time of day as the fraction.
    public static double Serial(DateTimeOffset d) => Cal.Local(d).DateTime.ToOADate();

    // ponytail: single letters cover the 11 columns we write.
    public static string Column(int i) => ((char)('A' + i)).ToString();

    // MARK: Styles

    enum Align { None, Indent, Left, Right }

    /// Number format, then indexes into `fonts`, `fills` and `borders`.
    static (string? Format, int Font, int Fill, int Border, Align Align) Spec(Style s)
    {
        const string signed = "+0.00;-0.00;0.00";
        const int fill = 2, border = 1;
        return s switch
        {
            Style.Plain => (null, 0, 0, 0, Align.None),
            Style.Bold => (null, 1, 0, 0, Align.None),
            Style.Date => ("ddd yyyy-mm-dd", 0, 0, 0, Align.None),
            Style.Time => ("h:mm", 0, 0, 0, Align.None),
            Style.Hours => ("0.00", 0, 0, 0, Align.None),
            Style.Balance => (signed, 0, 0, 0, Align.None),
            Style.TotalHours => ("0.00", 1, 0, 0, Align.None),
            Style.TotalBalance => (signed, 1, 0, 0, Align.None),
            Style.Title => (null, 2, 0, 0, Align.None),
            Style.Subtitle => (null, 3, 0, 0, Align.None),
            Style.Section => (null, 4, 0, 0, Align.None),
            Style.CardLabel => (null, 5, fill, border, Align.Indent),
            Style.CardHours => ("0.0\" h\"", 6, fill, border, Align.Indent),
            Style.CardBank => ("[Color10]+0.0\" h\";[Red]-0.0\" h\";0.0\" h\"", 6, fill, border, Align.Indent),
            Style.CardDays => ("0", 6, fill, border, Align.Indent),
            Style.CardCaption => (null, 7, fill, border, Align.Indent),
            Style.Head => (null, 8, 0, 2, Align.None),
            Style.HeadRight => (null, 8, 0, 2, Align.Right),
            Style.Month => ("mmm yyyy", 0, 0, 3, Align.Left),
            Style.RowText => (null, 0, 0, 3, Align.None),
            Style.RowHours => ("0.0", 0, 0, 3, Align.None),
            Style.RowBalance => ("[Color10]+0.0;[Red]-0.0;0.0", 0, 0, 3, Align.None),
            Style.RowCount => ("0", 0, 0, 3, Align.None),
            Style.RowShare => ("0%", 0, 0, 3, Align.None),
            _ => (";;;", 0, 0, 3, Align.None),  // RowBar: the data bar alone; Excel 2010+ ignores showValue="0"
        };
    }

    static readonly (bool Bold, int Size, string Color)[] Fonts =
    [
        (false, 11, "1F2937"), (true, 11, "1F2937"),  // body
        (true, 22, "111827"), (false, 11, "6B7280"), (true, 13, "111827"),  // title, subtitle, section
        (false, 9, "6B7280"), (true, 22, "111827"), (false, 9, "9CA3AF"),  // card label, value, caption
        (true, 9, "6B7280"),  // dashboard table header
    ];

    static string Styles()
    {
        var all = Enum.GetValues<Style>();
        var formats = all.Select(s => Spec(s).Format).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();
        int Id(string? f) => f == null ? 0 : 164 + formats.IndexOf(f);
        string Line(string side, string style, string color) => $"<{side} style=\"{style}\"><color rgb=\"FF{color}\"/></{side}>";
        var sb = new StringBuilder(Head).Append($"<styleSheet xmlns=\"{Main}\"><numFmts count=\"{formats.Count}\">");
        foreach (var f in formats) sb.Append($"<numFmt numFmtId=\"{Id(f)}\" formatCode=\"{Escape(f)}\"/>");
        sb.Append($"</numFmts><fonts count=\"{Fonts.Length}\">");
        foreach (var f in Fonts) sb.Append($"<font>{(f.Bold ? "<b/>" : "")}<sz val=\"{f.Size}\"/><color rgb=\"FF{f.Color}\"/><name val=\"Aptos\"/></font>");
        sb.Append("</fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>")
          .Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF3F4F6\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"4\">")
          .Append("<border><left/><right/><top/><bottom/><diagonal/></border>")
          // Thick white sides on the gray cards read as gutters between them.
          .Append("<border>" + Line("left", "thick", "FFFFFF") + Line("right", "thick", "FFFFFF") + "<top/><bottom/><diagonal/></border>")
          .Append("<border><left/><right/><top/>" + Line("bottom", "thin", "D1D5DB") + "<diagonal/></border>")
          .Append("<border><left/><right/><top/>" + Line("bottom", "thin", "EEF0F3") + "<diagonal/></border>")
          .Append($"</borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"{all.Length}\">");
        foreach (var s in all)
        {
            var x = Spec(s);
            var align = x.Align switch
            {
                Align.Indent => "<alignment horizontal=\"left\" vertical=\"center\" indent=\"1\"/>",
                Align.Left => "<alignment horizontal=\"left\"/>",
                Align.Right => "<alignment horizontal=\"right\"/>",
                _ => "",
            };
            sb.Append($"<xf numFmtId=\"{Id(x.Format)}\" fontId=\"{x.Font}\" fillId=\"{x.Fill}\" borderId=\"{x.Border}\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"")
              .Append(align.Length == 0 ? "/>" : $" applyAlignment=\"1\">{align}</xf>");
        }
        return sb.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>").ToString();
    }

    // MARK: Parts

    public static string Escape(string s)
    {
        // Control characters aren't allowed in XML at all; one stray one would make Excel reject the file.
        var clean = new string(s.Where(c => c >= 0x20 || c == '\t' || c == '\n').ToArray());
        return clean.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
    const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const string Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
    const string DrawingML = "http://schemas.openxmlformats.org/drawingml/2006";
    const string X14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    static string BarId(int i) => $"{{00000000-0000-0000-0000-{i + 1:D12}}}";

    static string SheetXml(Sheet s, bool selected)
    {
        var x = new StringBuilder(Head).Append($"<worksheet xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheetViews><sheetView workbookViewId=\"0\"")
            .Append(s.Gridlines ? "" : " showGridLines=\"0\"").Append(selected ? " tabSelected=\"1\"" : "").Append('>');
        if (s.FrozenRows > 0)
            x.Append($"<pane ySplit=\"{s.FrozenRows}\" topLeftCell=\"A{s.FrozenRows + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        x.Append("</sheetView></sheetViews><cols>");
        for (var i = 0; i < s.Widths.Count; i++) x.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{Num(s.Widths[i])}\" customWidth=\"1\"/>");
        x.Append("</cols><sheetData>");
        for (var r = 0; r < s.Rows.Count; r++)
        {
            x.Append($"<row r=\"{r + 1}\"").Append(s.Heights.TryGetValue(r, out var h) ? $" ht=\"{Num(h)}\" customHeight=\"1\"" : "").Append('>');
            for (var c = 0; c < s.Rows[r].Count; c++)
            {
                var cellRef = Column(c) + (r + 1);
                x.Append(s.Rows[r][c] switch
                {
                    Cell.Text t => $"<c r=\"{cellRef}\" s=\"{(int)t.Style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(t.Value)}</t></is></c>",
                    Cell.Number n => $"<c r=\"{cellRef}\" s=\"{(int)n.Style}\"><v>{Num(n.Value)}</v></c>",
                    Cell.Formula f => $"<c r=\"{cellRef}\" s=\"{(int)f.Style}\"><f>{Escape(f.F)}</f><v>{Num(f.Cached)}</v></c>",
                    Cell.Blank b => $"<c r=\"{cellRef}\" s=\"{(int)b.Style}\"/>",
                    _ => "",
                });
            }
            x.Append("</row>");
        }
        x.Append("</sheetData>");
        if (s.Merges.Count > 0)
            x.Append($"<mergeCells count=\"{s.Merges.Count}\">").Append(string.Concat(s.Merges.Select(m => $"<mergeCell ref=\"{m}\"/>"))).Append("</mergeCells>");
        for (var i = 0; i < s.Bars.Count; i++)
            x.Append($"<conditionalFormatting sqref=\"{s.Bars[i]}\"><cfRule type=\"dataBar\" priority=\"{i + 1}\"><dataBar showValue=\"0\"><cfvo type=\"num\" val=\"0\"/><cfvo type=\"num\" val=\"1\"/><color rgb=\"FF2563EB\"/></dataBar><extLst><ext uri=\"{{B025F937-C7B1-47D3-B67F-A62EFF666E3E}}\" xmlns:x14=\"{X14}\"><x14:id>{BarId(i)}</x14:id></ext></extLst></cfRule></conditionalFormatting>");
        if (s.Chart != null) x.Append("<drawing r:id=\"rId1\"/>");
        if (s.Table != null) x.Append("<tableParts count=\"1\"><tablePart r:id=\"rId2\"/></tableParts>");
        if (s.Bars.Count > 0)
        {
            // Excel 2010 extension: solid, flat bars instead of the 2007 gradient.
            x.Append($"<extLst><ext uri=\"{{78C0D931-6437-407d-A8EE-F0AAD7539E65}}\" xmlns:x14=\"{X14}\"><x14:conditionalFormattings>");
            for (var i = 0; i < s.Bars.Count; i++)
                x.Append($"<x14:conditionalFormatting xmlns:xm=\"http://schemas.microsoft.com/office/excel/2006/main\"><x14:cfRule type=\"dataBar\" id=\"{BarId(i)}\"><x14:dataBar minLength=\"0\" maxLength=\"100\" gradient=\"0\"><x14:cfvo type=\"num\"><xm:f>0</xm:f></x14:cfvo><x14:cfvo type=\"num\"><xm:f>1</xm:f></x14:cfvo><x14:negativeFillColor rgb=\"FFDC2626\"/><x14:axisColor rgb=\"FF000000\"/></x14:dataBar></x14:cfRule><xm:sqref>{s.Bars[i]}</xm:sqref></x14:conditionalFormatting>");
            x.Append("</x14:conditionalFormattings></ext></extLst>");
        }
        return x.Append("</worksheet>").ToString();
    }

    static string TableXml(Table t, int id)
    {
        var last = Column(t.Columns.Count - 1);
        var data = 1 + t.Rows;
        var totals = t.Sums.Count > 0;
        var sb = new StringBuilder(Head)
            .Append($"<table xmlns=\"{Main}\" id=\"{id}\" name=\"{t.Name}\" displayName=\"{t.Name}\" ref=\"A1:{last}{data + (totals ? 1 : 0)}\"")
            .Append(totals ? " totalsRowCount=\"1\"" : " totalsRowShown=\"0\"")
            .Append($"><autoFilter ref=\"A1:{last}{data}\"/><tableColumns count=\"{t.Columns.Count}\">");
        for (var i = 0; i < t.Columns.Count; i++)
        {
            var total = !totals ? "" : i == 0 ? " totalsRowLabel=\"Total\"" : t.Sums.Contains(i) ? " totalsRowFunction=\"sum\"" : "";
            sb.Append($"<tableColumn id=\"{i + 1}\" name=\"{Escape(t.Columns[i])}\"{total}/>");
        }
        return sb.Append("</tableColumns><tableStyleInfo name=\"TableStyleLight9\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/></table>").ToString();
    }

    static string Drawing(Chart c) =>
        Head + $"<xdr:wsDr xmlns:xdr=\"{DrawingML}/spreadsheetDrawing\" xmlns:a=\"{DrawingML}/main\"><xdr:twoCellAnchor editAs=\"oneCell\">"
        + $"<xdr:from><xdr:col>{c.From.Col}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{c.From.Row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"
        + $"<xdr:to><xdr:col>{c.To.Col}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{c.To.Row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>"
        + "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart 1\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>"
        + $"<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"{DrawingML}/chart\">"
        + $"<c:chart xmlns:c=\"{DrawingML}/chart\" xmlns:r=\"{Rel}\" r:id=\"rId1\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>";

    static string ChartXml(Chart c)
    {
        string Cache(IReadOnlyList<double> values, string format) =>
            $"<c:numCache><c:formatCode>{format}</c:formatCode><c:ptCount val=\"{values.Count}\"/>"
            + string.Concat(values.Select((v, i) => $"<c:pt idx=\"{i}\"><c:v>{Num(v)}</c:v></c:pt>")) + "</c:numCache>";
        string Solid(string rgb) => $"<a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill>";
        string Line(string? rgb) => "<c:spPr><a:ln>" + (rgb != null ? Solid(rgb) : "<a:noFill/>") + "</a:ln></c:spPr>";
        var text = $"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"900\">{Solid("6B7280")}</a:defRPr></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr>";
        return Head + $"<c:chartSpace xmlns:c=\"{DrawingML}/chart\" xmlns:a=\"{DrawingML}/main\" xmlns:r=\"{Rel}\"><c:roundedCorners val=\"0\"/>"
            + "<c:chart><c:autoTitleDeleted val=\"1\"/><c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>"
            + string.Concat(c.Series.Select((s, i) =>
                $"<c:ser><c:idx val=\"{i}\"/><c:order val=\"{i}\"/><c:tx><c:v>{Escape(s.Name)}</c:v></c:tx><c:spPr>{Solid(s.Color)}</c:spPr><c:invertIfNegative val=\"0\"/>"
                + $"<c:cat><c:numRef><c:f>{c.Categories.Ref}</c:f>{Cache(c.Categories.Values, "mmm yy")}</c:numRef></c:cat>"
                + $"<c:val><c:numRef><c:f>{s.Ref}</c:f>{Cache(s.Values, "0.0")}</c:numRef></c:val></c:ser>"))
            + "<c:gapWidth val=\"80\"/><c:overlap val=\"-10\"/><c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>"
            + "<c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:numFmt formatCode=\"mmm yy\" sourceLinked=\"0\"/>"
            + $"<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>{Line("D1D5DB")}<c:crossAx val=\"2\"/><c:crosses val=\"autoZero\"/>"
            + "<c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>"
            + $"<c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:majorGridlines>{Line("EEF0F3")}</c:majorGridlines>"
            + $"<c:numFmt formatCode=\"0\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>{Line(null)}"
            + "<c:crossAx val=\"1\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx></c:plotArea>"
            + "<c:legend><c:legendPos val=\"t\"/><c:overlay val=\"0\"/></c:legend><c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>"
            + $"<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>{text}</c:chartSpace>";
    }

    public static byte[] Workbook(IReadOnlyList<Sheet> sheets)
    {
        const string type = "application/vnd.openxmlformats-officedocument";
        string Relation(string id, string kind, string target) => $"<Relationship Id=\"{id}\" Type=\"{Rel}/{kind}\" Target=\"{target}\"/>";
        string Override(string part, string kind) => $"<Override PartName=\"/xl/{part}\" ContentType=\"{type}.{kind}+xml\"/>";
        var files = new List<(string, string)>();
        var types = Override("workbook.xml", "spreadsheetml.sheet.main") + Override("styles.xml", "spreadsheetml.styles");
        for (var i = 0; i < sheets.Count; i++)
        {
            var s = sheets[i];
            var n = i + 1;
            files.Add(($"xl/worksheets/sheet{n}.xml", SheetXml(s, i == 0)));
            types += Override($"worksheets/sheet{n}.xml", "spreadsheetml.worksheet");
            var rels = "";
            if (s.Chart is { } c)
            {
                rels += Relation("rId1", "drawing", $"../drawings/drawing{n}.xml");
                files.Add(($"xl/drawings/drawing{n}.xml", Drawing(c)));
                files.Add(($"xl/drawings/_rels/drawing{n}.xml.rels", Head + $"<Relationships xmlns=\"{Pkg}\">{Relation("rId1", "chart", $"../charts/chart{n}.xml")}</Relationships>"));
                files.Add(($"xl/charts/chart{n}.xml", ChartXml(c)));
                types += Override($"drawings/drawing{n}.xml", "drawing") + Override($"charts/chart{n}.xml", "drawingml.chart");
            }
            if (s.Table is { } t)
            {
                rels += Relation("rId2", "table", $"../tables/table{n}.xml");
                files.Add(($"xl/tables/table{n}.xml", TableXml(t, n)));
                types += Override($"tables/table{n}.xml", "spreadsheetml.table");
            }
            if (rels.Length > 0) files.Add(($"xl/worksheets/_rels/sheet{n}.xml.rels", Head + $"<Relationships xmlns=\"{Pkg}\">{rels}</Relationships>"));
        }
        var ids = Enumerable.Range(1, sheets.Count).ToList();
        var parts = new List<(string, string)>
        {
            ("[Content_Types].xml", Head + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" + types + "</Types>"),
            ("_rels/.rels", Head + $"<Relationships xmlns=\"{Pkg}\">{Relation("rId1", "officeDocument", "xl/workbook.xml")}</Relationships>"),
            ("xl/workbook.xml", Head + $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets>"
                + string.Concat(ids.Select(id => $"<sheet name=\"{Escape(sheets[id - 1].Name)}\" sheetId=\"{id}\" r:id=\"rId{id}\"/>"))
                + "</sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>"),
            ("xl/_rels/workbook.xml.rels", Head + $"<Relationships xmlns=\"{Pkg}\">"
                + string.Concat(ids.Select(id => Relation($"rId{id}", "worksheet", $"worksheets/sheet{id}.xml")))
                + Relation($"rId{ids.Count + 1}", "styles", "styles.xml") + "</Relationships>"),
            ("xl/styles.xml", Styles()),
        };
        parts.AddRange(files);
        return Zip.Stored(parts.Select(p => (p.Item1, Encoding.UTF8.GetBytes(p.Item2))).ToList());
    }
}

public static class Zip
{
    /// A zip archive with every file stored uncompressed, which is all a .xlsx needs.
    public static byte[] Stored(IReadOnlyList<(string Name, byte[] Data)> files)
    {
        using var output = new MemoryStream();
        using var central = new MemoryStream();
        var o = new BinaryWriter(output);
        var c = new BinaryWriter(central);
        foreach (var (name, data) in files)
        {
            var n = Encoding.UTF8.GetBytes(name);
            var crc = Crc32(data);
            var size = (uint)data.Length;
            var offset = (uint)output.Length;
            // Signature, version 2.0, no flags, stored, DOS time 00:00 and date 1980-01-01, CRC, sizes, name length, no extra.
            o.Write(0x04034b50u); o.Write((ushort)20); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)0x21);
            o.Write(crc); o.Write(size); o.Write(size); o.Write((ushort)n.Length); o.Write((ushort)0);
            o.Write(n); o.Write(data);
            // Same again for the central directory, plus no comment/disk/attributes and where the local header sits.
            c.Write(0x02014b50u); c.Write((ushort)20); c.Write((ushort)20); c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0x21);
            c.Write(crc); c.Write(size); c.Write(size); c.Write((ushort)n.Length);
            c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0); c.Write(0u); c.Write(offset);
            c.Write(n);
        }
        c.Flush(); o.Flush();
        var directory = (uint)output.Length;
        o.Write(central.ToArray());
        o.Write(0x06054b50u); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)files.Count); o.Write((ushort)files.Count);
        o.Write((uint)central.Length); o.Write(directory); o.Write((ushort)0);
        o.Flush();
        return output.ToArray();
    }

    public static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
