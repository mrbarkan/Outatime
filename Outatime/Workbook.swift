import Foundation

/// A small Excel (.xlsx) writer: inline strings, a fixed style sheet, Excel tables, a column chart and solid data bars,
/// in an uncompressed zip. Formulas carry cached values for Quick Look and Numbers; Excel recalculates them on open.
nonisolated enum XLSX {
    enum Cell {
        case text(String, Style = .plain)
        case number(Double, Style)
        case formula(String, cached: Double, Style)
        /// No value, only formatting (the second half of a merged card).
        case blank(Style)
        case empty
    }

    struct Table {
        var name: String
        var columns: [String]
        /// Data rows under the header; at least one.
        var rows: Int
        /// Columns summed in a totals row; none, no totals row.
        var sums: [Int]
    }

    struct Chart {
        var from: (col: Int, row: Int), to: (col: Int, row: Int)
        var categories: (ref: String, values: [Double])
        var series: [(name: String, ref: String, values: [Double], color: String)]
    }

    struct Sheet {
        var name: String
        var rows: [[Cell]] = []
        var widths: [Double] = []
        var heights: [Int: Double] = [:]
        var merges: [String] = []
        var frozenRows = 0
        var gridlines = true
        var table: Table?
        var chart: Chart?
        /// Ranges holding 0…1 shares, drawn as bars without the number.
        var bars: [String] = []

        mutating func put(_ row: Int, _ col: Int, _ cell: Cell) {
            while rows.count <= row { rows.append([]) }
            while rows[row].count <= col { rows[row].append(.empty) }
            rows[row][col] = cell
        }
    }

    /// A sheet that is one Excel table: header row, `rows`, and with `sums` a totals row. Pasting rows right under
    /// it grows the table, which is how a month report joins the master workbook.
    static func tableSheet(_ name: String, table: String, columns: [String], rows: [[Cell]],
                           sums: [(column: Int, total: Double, style: Style)] = [], widths: [Double]) -> Sheet {
        var s = Sheet(name: name, rows: [columns.map { .text($0) }] + (rows.isEmpty ? [[]] : rows), widths: widths, frozenRows: 1)
        if !sums.isEmpty {
            var total = [Cell](repeating: .empty, count: columns.count)
            total[0] = .text("Total", .bold)
            for c in sums { total[c.column] = .formula("SUBTOTAL(109,\(table)[\(columns[c.column])])", cached: c.total, c.style) }
            s.rows.append(total)
        }
        s.table = Table(name: table, columns: columns, rows: max(rows.count, 1), sums: sums.map(\.column))
        return s
    }

    /// Excel's day number for a moment in the local time zone: days since 1899-12-30, the time of day as the fraction.
    static func serial(_ d: Date) -> Double {
        (d.timeIntervalSince1970 + Double(TimeZone.current.secondsFromGMT(for: d))) / 86400 + 25569
    }

    // ponytail: single letters cover the 11 columns we write.
    static func column(_ i: Int) -> String { String(UnicodeScalar(UInt8(65 + i))) }

    // MARK: Styles

    /// Raw values are indexes into `cellXfs`.
    enum Style: Int, CaseIterable {
        case plain, bold, date, time, hours, balance, totalHours, totalBalance
        case title, subtitle, section, cardLabel, cardHours, cardBank, cardDays, cardCaption
        case head, headRight, month, rowText, rowHours, rowBalance, rowCount, rowShare, rowBar

        enum Align { case none, indent, left, right }

        /// Number format, then indexes into `fonts`, `fills` and `borders`.
        var spec: (format: String?, font: Int, fill: Int, border: Int, align: Align) {
            let signed = "+0.00;-0.00;0.00", card = (fill: 2, border: 1)
            return switch self {
            case .plain: (nil, 0, 0, 0, .none)
            case .bold: (nil, 1, 0, 0, .none)
            case .date: ("ddd yyyy-mm-dd", 0, 0, 0, .none)
            case .time: ("h:mm", 0, 0, 0, .none)
            case .hours: ("0.00", 0, 0, 0, .none)
            case .balance: (signed, 0, 0, 0, .none)
            case .totalHours: ("0.00", 1, 0, 0, .none)
            case .totalBalance: (signed, 1, 0, 0, .none)
            case .title: (nil, 2, 0, 0, .none)
            case .subtitle: (nil, 3, 0, 0, .none)
            case .section: (nil, 4, 0, 0, .none)
            case .cardLabel: (nil, 5, card.fill, card.border, .indent)
            case .cardHours: (#"0.0" h""#, 6, card.fill, card.border, .indent)
            case .cardBank: (#"[Color10]+0.0" h";[Red]-0.0" h";0.0" h""#, 6, card.fill, card.border, .indent)
            case .cardDays: ("0", 6, card.fill, card.border, .indent)
            case .cardCaption: (nil, 7, card.fill, card.border, .indent)
            case .head: (nil, 8, 0, 2, .none)
            case .headRight: (nil, 8, 0, 2, .right)
            case .month: ("mmm yyyy", 0, 0, 3, .left)
            case .rowText: (nil, 0, 0, 3, .none)
            case .rowHours: ("0.0", 0, 0, 3, .none)
            case .rowBalance: ("[Color10]+0.0;[Red]-0.0;0.0", 0, 0, 3, .none)
            case .rowCount: ("0", 0, 0, 3, .none)
            case .rowShare: ("0%", 0, 0, 3, .none)
            case .rowBar: (";;;", 0, 0, 3, .none)  // the data bar alone; Excel 2010+ ignores showValue="0"
            }
        }
    }

    private static let fonts: [(bold: Bool, size: Int, color: String)] = [
        (false, 11, "1F2937"), (true, 11, "1F2937"),  // body
        (true, 22, "111827"), (false, 11, "6B7280"), (true, 13, "111827"),  // title, subtitle, section
        (false, 9, "6B7280"), (true, 22, "111827"), (false, 9, "9CA3AF"),  // card label, value, caption
        (true, 9, "6B7280"),  // dashboard table header
    ]

    private static var styles: String {
        let formats = Array(Set(Style.allCases.compactMap(\.spec.format))).sorted()
        let id = { (f: String?) in f.map { 164 + formats.firstIndex(of: $0)! } ?? 0 }
        let line = { (side: String, style: String, color: String) in #"<\#(side) style="\#(style)"><color rgb="FF\#(color)"/></\#(side)>"# }
        return head + #"<styleSheet xmlns="\#(main)"><numFmts count="\#(formats.count)">"#
            + formats.map { #"<numFmt numFmtId="\#(id($0))" formatCode="\#(escape($0))"/>"# }.joined()
            + #"</numFmts><fonts count="\#(fonts.count)">"#
            + fonts.map { #"<font>\#($0.bold ? "<b/>" : "")<sz val="\#($0.size)"/><color rgb="FF\#($0.color)"/><name val="Aptos"/></font>"# }.joined()
            + #"</fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>"#
            + #"<fill><patternFill patternType="solid"><fgColor rgb="FFF3F4F6"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="4">"#
            + "<border><left/><right/><top/><bottom/><diagonal/></border>"
            // Thick white sides on the gray cards read as gutters between them.
            + "<border>" + line("left", "thick", "FFFFFF") + line("right", "thick", "FFFFFF") + "<top/><bottom/><diagonal/></border>"
            + "<border><left/><right/><top/>" + line("bottom", "thin", "D1D5DB") + "<diagonal/></border>"
            + "<border><left/><right/><top/>" + line("bottom", "thin", "EEF0F3") + "<diagonal/></border>"
            + #"</borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="\#(Style.allCases.count)">"#
            + Style.allCases.map { s in
                let x = s.spec
                let align = switch x.align {
                case .none: ""
                case .indent: #"<alignment horizontal="left" vertical="center" indent="1"/>"#
                case .left: #"<alignment horizontal="left"/>"#
                case .right: #"<alignment horizontal="right"/>"#
                }
                return #"<xf numFmtId="\#(id(x.format))" fontId="\#(x.font)" fillId="\#(x.fill)" borderId="\#(x.border)" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1""#
                    + (align.isEmpty ? "/>" : #" applyAlignment="1">\#(align)</xf>"#)
            }.joined()
            + #"</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>"#
    }

    // MARK: Parts

    private static func escape(_ s: String) -> String {
        // Control characters aren't allowed in XML at all; one stray one would make Excel reject the file.
        String(String.UnicodeScalarView(s.unicodeScalars.filter { $0.value >= 0x20 || $0 == "\t" || $0 == "\n" }))
            .replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }

    private static let head = #"<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"#
    private static let main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    private static let rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    private static let pkg = "http://schemas.openxmlformats.org/package/2006/relationships"
    private static let drawingML = "http://schemas.openxmlformats.org/drawingml/2006"
    private static let x14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"

    private static func barID(_ i: Int) -> String { String(format: "{00000000-0000-0000-0000-%012d}", i + 1) }

    private static func xml(_ s: Sheet, selected: Bool) -> String {
        var x = head + #"<worksheet xmlns="\#(main)" xmlns:r="\#(rel)"><sheetViews><sheetView workbookViewId="0""#
            + (s.gridlines ? "" : #" showGridLines="0""#) + (selected ? #" tabSelected="1""# : "") + ">"
        if s.frozenRows > 0 {
            x += #"<pane ySplit="\#(s.frozenRows)" topLeftCell="A\#(s.frozenRows + 1)" activePane="bottomLeft" state="frozen"/>"#
        }
        x += "</sheetView></sheetViews><cols>"
        for (i, w) in s.widths.enumerated() { x += #"<col min="\#(i + 1)" max="\#(i + 1)" width="\#(w)" customWidth="1"/>"# }
        x += "</cols><sheetData>"
        for (r, cells) in s.rows.enumerated() {
            x += #"<row r="\#(r + 1)""# + (s.heights[r].map { #" ht="\#($0)" customHeight="1""# } ?? "") + ">"
            for (c, cell) in cells.enumerated() {
                let ref = column(c) + "\(r + 1)"
                switch cell {
                case let .text(t, style): x += #"<c r="\#(ref)" s="\#(style.rawValue)" t="inlineStr"><is><t xml:space="preserve">\#(escape(t))</t></is></c>"#
                case let .number(v, style): x += #"<c r="\#(ref)" s="\#(style.rawValue)"><v>\#(v)</v></c>"#
                case let .formula(f, v, style): x += #"<c r="\#(ref)" s="\#(style.rawValue)"><f>\#(escape(f))</f><v>\#(v)</v></c>"#
                case let .blank(style): x += #"<c r="\#(ref)" s="\#(style.rawValue)"/>"#
                case .empty: break
                }
            }
            x += "</row>"
        }
        x += "</sheetData>"
        if !s.merges.isEmpty {
            x += #"<mergeCells count="\#(s.merges.count)">"# + s.merges.map { #"<mergeCell ref="\#($0)"/>"# }.joined() + "</mergeCells>"
        }
        for (i, range) in s.bars.enumerated() {
            x += #"<conditionalFormatting sqref="\#(range)"><cfRule type="dataBar" priority="\#(i + 1)"><dataBar showValue="0"><cfvo type="num" val="0"/><cfvo type="num" val="1"/><color rgb="FF2563EB"/></dataBar><extLst><ext uri="{B025F937-C7B1-47D3-B67F-A62EFF666E3E}" xmlns:x14="\#(x14)"><x14:id>\#(barID(i))</x14:id></ext></extLst></cfRule></conditionalFormatting>"#
        }
        if s.chart != nil { x += #"<drawing r:id="rId1"/>"# }
        if s.table != nil { x += #"<tableParts count="1"><tablePart r:id="rId2"/></tableParts>"# }
        if !s.bars.isEmpty {
            // Excel 2010 extension: solid, flat bars instead of the 2007 gradient.
            x += #"<extLst><ext uri="{78C0D931-6437-407d-A8EE-F0AAD7539E65}" xmlns:x14="\#(x14)"><x14:conditionalFormattings>"#
                + s.bars.enumerated().map { i, range in
                    #"<x14:conditionalFormatting xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main"><x14:cfRule type="dataBar" id="\#(barID(i))"><x14:dataBar minLength="0" maxLength="100" gradient="0"><x14:cfvo type="num"><xm:f>0</xm:f></x14:cfvo><x14:cfvo type="num"><xm:f>1</xm:f></x14:cfvo><x14:negativeFillColor rgb="FFDC2626"/><x14:axisColor rgb="FF000000"/></x14:dataBar></x14:cfRule><xm:sqref>\#(range)</xm:sqref></x14:conditionalFormatting>"#
                }.joined() + "</x14:conditionalFormattings></ext></extLst>"
        }
        return x + "</worksheet>"
    }

    private static func xml(_ t: Table, id: Int) -> String {
        let last = column(t.columns.count - 1), data = 1 + t.rows
        let totals = !t.sums.isEmpty
        return head + #"<table xmlns="\#(main)" id="\#(id)" name="\#(t.name)" displayName="\#(t.name)" ref="A1:\#(last)\#(data + (totals ? 1 : 0))""#
            + (totals ? #" totalsRowCount="1""# : #" totalsRowShown="0""#) + #"><autoFilter ref="A1:\#(last)\#(data)"/><tableColumns count="\#(t.columns.count)">"#
            + t.columns.enumerated().map { i, name in
                let total = !totals ? "" : i == 0 ? #" totalsRowLabel="Total""# : t.sums.contains(i) ? #" totalsRowFunction="sum""# : ""
                return #"<tableColumn id="\#(i + 1)" name="\#(escape(name))"\#(total)/>"#
            }.joined()
            + #"</tableColumns><tableStyleInfo name="TableStyleLight9" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/></table>"#
    }

    private static func drawing(_ c: Chart) -> String {
        head + #"<xdr:wsDr xmlns:xdr="\#(drawingML)/spreadsheetDrawing" xmlns:a="\#(drawingML)/main"><xdr:twoCellAnchor editAs="oneCell">"#
            + #"<xdr:from><xdr:col>\#(c.from.col)</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>\#(c.from.row)</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"#
            + #"<xdr:to><xdr:col>\#(c.to.col)</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>\#(c.to.row)</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>"#
            + #"<xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="Chart 1"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>"#
            + #"<xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm><a:graphic><a:graphicData uri="\#(drawingML)/chart">"#
            + #"<c:chart xmlns:c="\#(drawingML)/chart" xmlns:r="\#(rel)" r:id="rId1"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>"#
    }

    private static func chart(_ c: Chart) -> String {
        let cache = { (values: [Double], format: String) in
            #"<c:numCache><c:formatCode>\#(format)</c:formatCode><c:ptCount val="\#(values.count)"/>"#
                + values.enumerated().map { #"<c:pt idx="\#($0)"><c:v>\#($1)</c:v></c:pt>"# }.joined() + "</c:numCache>"
        }
        let solid = { (rgb: String) in #"<a:solidFill><a:srgbClr val="\#(rgb)"/></a:solidFill>"# }
        let line = { (rgb: String?) in "<c:spPr><a:ln>" + (rgb.map(solid) ?? "<a:noFill/>") + "</a:ln></c:spPr>" }
        let text = #"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="900">\#(solid("6B7280"))</a:defRPr></a:pPr><a:endParaRPr lang="en-US"/></a:p></c:txPr>"#
        return head + #"<c:chartSpace xmlns:c="\#(drawingML)/chart" xmlns:a="\#(drawingML)/main" xmlns:r="\#(rel)"><c:roundedCorners val="0"/>"#
            + #"<c:chart><c:autoTitleDeleted val="1"/><c:plotArea><c:layout/><c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>"#
            + c.series.enumerated().map { i, s in
                #"<c:ser><c:idx val="\#(i)"/><c:order val="\#(i)"/><c:tx><c:v>\#(escape(s.name))</c:v></c:tx><c:spPr>\#(solid(s.color))</c:spPr><c:invertIfNegative val="0"/>"#
                    + #"<c:cat><c:numRef><c:f>\#(c.categories.ref)</c:f>\#(cache(c.categories.values, "mmm yy"))</c:numRef></c:cat>"#
                    + #"<c:val><c:numRef><c:f>\#(s.ref)</c:f>\#(cache(s.values, "0.0"))</c:numRef></c:val></c:ser>"#
            }.joined()
            + #"<c:gapWidth val="80"/><c:overlap val="-10"/><c:axId val="1"/><c:axId val="2"/></c:barChart>"#
            + #"<c:catAx><c:axId val="1"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:numFmt formatCode="mmm yy" sourceLinked="0"/>"#
            + #"<c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/>\#(line("D1D5DB"))<c:crossAx val="2"/><c:crosses val="autoZero"/>"#
            + #"<c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/><c:noMultiLvlLbl val="0"/></c:catAx>"#
            + #"<c:valAx><c:axId val="2"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:majorGridlines>\#(line("EEF0F3"))</c:majorGridlines>"#
            + #"<c:numFmt formatCode="0" sourceLinked="0"/><c:majorTickMark val="none"/><c:minorTickMark val="none"/><c:tickLblPos val="nextTo"/>\#(line(nil))"#
            + #"<c:crossAx val="1"/><c:crosses val="autoZero"/><c:crossBetween val="between"/></c:valAx></c:plotArea>"#
            + #"<c:legend><c:legendPos val="t"/><c:overlay val="0"/></c:legend><c:plotVisOnly val="1"/><c:dispBlanksAs val="gap"/></c:chart>"#
            + #"<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>\#(text)</c:chartSpace>"#
    }

    static func workbook(_ sheets: [Sheet]) -> Data {
        let type = "application/vnd.openxmlformats-officedocument"
        let relation = { (id: String, kind: String, target: String) in #"<Relationship Id="\#(id)" Type="\#(rel)/\#(kind)" Target="\#(target)"/>"# }
        let override = { (part: String, kind: String) in #"<Override PartName="/xl/\#(part)" ContentType="\#(type).\#(kind)+xml"/>"# }
        var files: [(String, String)] = [], types = override("workbook.xml", "spreadsheetml.sheet.main") + override("styles.xml", "spreadsheetml.styles")
        for (i, s) in sheets.enumerated() {
            let n = i + 1
            files.append(("xl/worksheets/sheet\(n).xml", xml(s, selected: i == 0)))
            types += override("worksheets/sheet\(n).xml", "spreadsheetml.worksheet")
            var rels = ""
            if let c = s.chart {
                rels += relation("rId1", "drawing", "../drawings/drawing\(n).xml")
                files.append(("xl/drawings/drawing\(n).xml", drawing(c)))
                files.append(("xl/drawings/_rels/drawing\(n).xml.rels", head + #"<Relationships xmlns="\#(pkg)">\#(relation("rId1", "chart", "../charts/chart\(n).xml"))</Relationships>"#))
                files.append(("xl/charts/chart\(n).xml", chart(c)))
                types += override("drawings/drawing\(n).xml", "drawing") + override("charts/chart\(n).xml", "drawingml.chart")
            }
            if let t = s.table {
                rels += relation("rId2", "table", "../tables/table\(n).xml")
                files.append(("xl/tables/table\(n).xml", xml(t, id: n)))
                types += override("tables/table\(n).xml", "spreadsheetml.table")
            }
            if !rels.isEmpty { files.append(("xl/worksheets/_rels/sheet\(n).xml.rels", head + #"<Relationships xmlns="\#(pkg)">\#(rels)</Relationships>"#)) }
        }
        let ids = sheets.indices.map { $0 + 1 }
        files = [
            ("[Content_Types].xml", head + #"<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>\#(types)</Types>"#),
            ("_rels/.rels", head + #"<Relationships xmlns="\#(pkg)">\#(relation("rId1", "officeDocument", "xl/workbook.xml"))</Relationships>"#),
            ("xl/workbook.xml", head + #"<workbook xmlns="\#(main)" xmlns:r="\#(rel)"><sheets>"#
                + zip(ids, sheets).map { #"<sheet name="\#(escape($1.name))" sheetId="\#($0)" r:id="rId\#($0)"/>"# }.joined()
                + #"</sheets><calcPr calcId="191029" fullCalcOnLoad="1"/></workbook>"#),
            ("xl/_rels/workbook.xml.rels", head + #"<Relationships xmlns="\#(pkg)">"#
                + ids.map { relation("rId\($0)", "worksheet", "worksheets/sheet\($0).xml") }.joined()
                + relation("rId\(ids.count + 1)", "styles", "styles.xml") + "</Relationships>"),
            ("xl/styles.xml", styles),
        ] + files
        return Zip.stored(files.map { ($0, Data($1.utf8)) })
    }
}

nonisolated enum Zip {
    /// A zip archive with every file stored uncompressed, which is all a .xlsx needs.
    static func stored(_ files: [(name: String, data: Data)]) -> Data {
        var out = Data(), central = Data()
        for (name, data) in files {
            let n = Data(name.utf8), crc = crc32(data), size = UInt32(data.count), offset = UInt32(out.count)
            // Signature, version 2.0, no flags, stored, DOS time 00:00 and date 1980-01-01, CRC, sizes, name length, no extra.
            out.le32(0x0403_4b50); out.le16(20); out.le16(0); out.le16(0); out.le16(0); out.le16(0x21)
            out.le32(crc); out.le32(size); out.le32(size); out.le16(UInt16(n.count)); out.le16(0)
            out += n + data
            // Same again for the central directory, plus no comment/disk/attributes and where the local header sits.
            central.le32(0x0201_4b50); central.le16(20); central.le16(20); central.le16(0); central.le16(0); central.le16(0); central.le16(0x21)
            central.le32(crc); central.le32(size); central.le32(size); central.le16(UInt16(n.count))
            central.le16(0); central.le16(0); central.le16(0); central.le16(0); central.le32(0); central.le32(offset)
            central += n
        }
        let directory = UInt32(out.count)
        out += central
        out.le32(0x0605_4b50); out.le16(0); out.le16(0); out.le16(UInt16(files.count)); out.le16(UInt16(files.count))
        out.le32(UInt32(central.count)); out.le32(directory); out.le16(0)
        return out
    }

    static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xFFFF_FFFF
        for byte in data {
            crc ^= UInt32(byte)
            for _ in 0..<8 { crc = crc & 1 == 1 ? (crc >> 1) ^ 0xEDB8_8320 : crc >> 1 }
        }
        return ~crc
    }
}

nonisolated private extension Data {
    mutating func le16(_ v: UInt16) { Swift.withUnsafeBytes(of: v.littleEndian) { append(contentsOf: $0) } }
    mutating func le32(_ v: UInt32) { Swift.withUnsafeBytes(of: v.littleEndian) { append(contentsOf: $0) } }
}
