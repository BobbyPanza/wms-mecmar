using WMS.Models;

namespace WMS.Services;

// Singleton: tutti i client condividono lo stesso stato demo (mutable in-memory)
public class MockWmsService
{
    // ─── Anagrafiche interne ─────────────────────────────────────────────────

    private record MockArticle(string Code, string Desc, string UoM, decimal MinStock);
    private record MockWarehouse(string Code, string Desc, string Type);
    private record MockLocation(string MgCod, string LcCod, string Desc, int Lane, int HPos, int VPos, bool IsAcceptance = false, bool IsFiscal = true);
    private record MockStock(string PaCod, string MgCod, string LcCod, decimal Qty, string Priority, bool IsMain);

    // ─── Operatori mock ───────────────────────────────────────────────────────

    public static readonly List<(string Code, string Name, string Role, string Pin)> Operators =
    [
        ("OP01", "Marco Rossi", "OP", "1234"),
        ("OP02", "Luigi Bianchi", "OP", "4321"),
        ("SUP01", "Maria Verdi", "SUP", "9999"),
        ("ADM", "Admin WMS", "ADM", "0000"),
    ];

    // ─── Dati master ─────────────────────────────────────────────────────────

    private readonly List<MockArticle> _articles =
    [
        new("BRU-2000", "BRUCIATORE GAS 2.0 MW", "PZ", 5),
        new("BRU-0500", "BRUCIATORE GAS 0.5 MW", "PZ", 5),
        new("TEN-DN25", "TENUTA MECCANICA DN25", "PZ", 10),
        new("RES-3KW", "RESISTENZA ELETTRICA 3KW", "PZ", 2),
        new("VIT-M0820", "VITE M8x20 ZN", "PZ", 200),
        new("GUA-SIL50", "GUARNIZIONE SILICONE 50mm", "PZ", 20),
        new("COL-DX150", "COLLETTORE DISTRIBUZIONE DN150", "PZ", 1),
        new("POM-CEN02", "POMPA CENTRIFUGA 2HP", "PZ", 2),
        new("VAL-DN40", "VALVOLA A SFERA DN40", "PZ", 5),
        new("FLT-OLE01", "FILTRO OLIO MOTORE", "PZ", 10),
    ];

    private readonly List<MockWarehouse> _warehouses =
    [
        new("MAG", "Magazzino Principale", "M"),
        new("TRANS", "Transito", "T"),
        new("COLL", "Collaudo", "C"),
    ];

    private readonly List<MockLocation> _locations =
    [
        new("MAG", "A-01-01", "Corsia A / Scaffale 01 / Ripiano 1", 1, 1, 1),
        new("MAG", "A-01-02", "Corsia A / Scaffale 01 / Ripiano 2", 1, 1, 2),
        new("MAG", "A-01-03", "Corsia A / Scaffale 01 / Ripiano 3", 1, 1, 3),
        new("MAG", "B-02-01", "Corsia B / Scaffale 02 / Ripiano 1", 2, 2, 1),
        new("MAG", "B-02-02", "Corsia B / Scaffale 02 / Ripiano 2", 2, 2, 2),
        new("MAG", "C-01-03", "Corsia C / Scaffale 01 / Ripiano 3", 3, 1, 3),
        new("MAG", "C-02-01", "Corsia C / Scaffale 02 / Ripiano 1", 3, 2, 1),
        new("MAG", "BULK-01", "Zona Bulk / Scaffale 01", 5, 1, 1),
        new("TRANS", "TRANS-01", "Locazione Transito 01", 1, 1, 1, IsAcceptance: true),
        new("TRANS", "TRANS-02", "Locazione Transito 02", 1, 2, 1, IsAcceptance: true),
        new("COLL", "COLL-01", "Zona Collaudo", 1, 1, 1),
    ];

    // Giacenza per locazione (mutable — si aggiorna con i movimenti demo)
    private readonly List<MockStock> _stocks =
    [
        new("BRU-2000", "MAG", "A-01-01", 8, "P", true),
        new("BRU-2000", "MAG", "B-02-01", 4, "S", false),
        new("BRU-0500", "MAG", "A-01-02", 3, "P", true),
        new("TEN-DN25", "MAG", "C-01-03", 45, "P", true),
        new("RES-3KW", "MAG", "A-01-01", 0, "P", true),
        new("VIT-M0820", "MAG", "BULK-01", 1250, "P", true),
        new("GUA-SIL50", "MAG", "C-02-01", 89, "P", true),
        new("COL-DX150", "MAG", "B-02-01", 2, "P", true),
        new("POM-CEN02", "MAG", "C-01-03", 7, "P", true),
        new("VAL-DN40", "MAG", "B-02-02", 14, "P", true),
        new("FLT-OLE01", "MAG", "A-01-03", 6, "P", true),
    ];

    private readonly List<MovementDto> _movements = GenerateMockMovements();

    // ─── Ordini di produzione ─────────────────────────────────────────────────

    private readonly List<ProductionOrderDto> _productionOrders;

    // ─── Liste di prelievo v2 ────────────────────────────────────────────────

    private readonly List<PickListV2Dto> _pickListsV2;

    // ─── Accettazione ─────────────────────────────────────────────────────────

    private readonly List<AcceptanceDocDto> _acceptanceDocs;


    // ─── Costruttore ─────────────────────────────────────────────────────────

    public MockWmsService()
    {
        _productionOrders =
        [
            new(
                Code: "OP-2025-0342",
                Description: "BRUCIATORE GAS 2.0 MW — lotto 5 pz",
                DueDate: DateTime.Today.AddDays(3),
                Status: "In corso",
                TotalItems: 4,
                PickedItems: 1,
                Items:
                [
                    new(Guid.NewGuid(), "BRU-2000", "BRUCIATORE GAS 2.0 MW", "PZ", 5, 0, "MAG", "A-01-01", 8, PickStatus.Pending),
                    new(Guid.NewGuid(), "TEN-DN25", "TENUTA MECCANICA DN25", "PZ", 10, 10, "MAG", "C-01-03", 45, PickStatus.Completed),
                    new(Guid.NewGuid(), "GUA-SIL50", "GUARNIZIONE SILICONE 50mm", "PZ", 5, 0, "MAG", "C-02-01", 89, PickStatus.Pending),
                    new(Guid.NewGuid(), "VIT-M0820", "VITE M8x20 ZN", "PZ", 24, 0, "MAG", "BULK-01", 1250, PickStatus.Pending),
                ]
            ),
            new(
                Code: "OP-2025-0356",
                Description: "POMPA CENTRIFUGA 2HP — lotto 2 pz",
                DueDate: DateTime.Today.AddDays(7),
                Status: "Aperto",
                TotalItems: 3,
                PickedItems: 0,
                Items:
                [
                    new(Guid.NewGuid(), "POM-CEN02", "POMPA CENTRIFUGA 2HP", "PZ", 2, 0, "MAG", "C-01-03", 7, PickStatus.Pending),
                    new(Guid.NewGuid(), "VAL-DN40", "VALVOLA A SFERA DN40", "PZ", 4, 0, "MAG", "B-02-02", 14, PickStatus.Pending),
                    new(Guid.NewGuid(), "FLT-OLE01", "FILTRO OLIO MOTORE", "PZ", 2, 0, "MAG", "A-01-03", 6, PickStatus.Pending),
                ]
            ),
            new(
                Code: "OP-2025-0361",
                Description: "COLLETTORE DISTRIBUZIONE DN150 — lotto 1 pz",
                DueDate: DateTime.Today.AddDays(1),
                Status: "Urgente",
                TotalItems: 2,
                PickedItems: 0,
                Items:
                [
                    new(Guid.NewGuid(), "COL-DX150", "COLLETTORE DISTRIBUZIONE DN150", "PZ", 1, 0, "MAG", "B-02-01", 2, PickStatus.Pending),
                    new(Guid.NewGuid(), "GUA-SIL50", "GUARNIZIONE SILICONE 50mm", "PZ", 3, 0, "MAG", "C-02-01", 89, PickStatus.Pending),
                ]
            ),
        ];

        _pickListsV2 =
        [
            new(
                Code: "LIST-2025-0015",
                Description: "Picking OC-2025-0421 — Cliente Rossi Impianti",
                Reference: "DDT-2025-0067",
                CreatedAt: DateTime.Today.AddHours(-2),
                Status: "In corso",
                CustomAttributeLabel: "Tipo Handling",
                Items:
                [
                    new() { ArticleCode="VIT-M0820", ArticleDesc="VITE M8x20 ZN",           UoM="PZ",  PlannedQty=50,  PickedQty=50, MainLocationCode="BULK-01", MainLocationDesc="Zona Bulk / Scaffale 01",              BestStockLocationCode="BULK-01", BestStockLocationDesc="Zona Bulk / Scaffale 01",              BestStockQty=1250, CustomAttribute="Palette",  Status=PickStatus.Completed },
                    new() { ArticleCode="GUA-SIL50", ArticleDesc="GUARNIZIONE SILICONE 50mm", UoM="PZ", PlannedQty=20,  PickedQty=0,  MainLocationCode="C-02-01", MainLocationDesc="Corsia C / Scaffale 02 / Ripiano 1",    BestStockLocationCode="C-02-01", BestStockLocationDesc="Corsia C / Scaffale 02 / Ripiano 1",    BestStockQty=89,   CustomAttribute="Collo",    Status=PickStatus.Pending  },
                    new() { ArticleCode="TEN-DN25",  ArticleDesc="TENUTA MECCANICA DN25",     UoM="PZ", PlannedQty=5,   PickedQty=0,  MainLocationCode="C-01-03", MainLocationDesc="Corsia C / Scaffale 01 / Ripiano 3",    BestStockLocationCode="C-01-03", BestStockLocationDesc="Corsia C / Scaffale 01 / Ripiano 3",    BestStockQty=45,   CustomAttribute="Collo",    Status=PickStatus.Pending  },
                ]
            ),
            new(
                Code: "LIST-2025-0016",
                Description: "Riassortimento scaffali zona A",
                Reference: "INT-2025-0015",
                CreatedAt: DateTime.Today.AddHours(-5),
                Status: "Aperta",
                CustomAttributeLabel: "Zona dest.",
                Items:
                [
                    new() { ArticleCode="BRU-0500",  ArticleDesc="BRUCIATORE GAS 0.5 MW",    UoM="PZ", PlannedQty=2,   PickedQty=0,  MainLocationCode="A-01-02", MainLocationDesc="Corsia A / Scaffale 01 / Ripiano 2",    BestStockLocationCode="A-01-02", BestStockLocationDesc="Corsia A / Scaffale 01 / Ripiano 2",    BestStockQty=3,    CustomAttribute="Zona A",   Status=PickStatus.Pending  },
                    new() { ArticleCode="RES-3KW",   ArticleDesc="RESISTENZA ELETTRICA 3KW",  UoM="PZ", PlannedQty=3,   PickedQty=0,  MainLocationCode="A-01-01", MainLocationDesc="Corsia A / Scaffale 01 / Ripiano 1",    BestStockLocationCode="A-01-01", BestStockLocationDesc="Corsia A / Scaffale 01 / Ripiano 1",    BestStockQty=0,    CustomAttribute="Zona A",   Status=PickStatus.Pending  },
                ]
            ),
        ];

        _acceptanceDocs =
        [
            new(
                DocumentRef: "DDT-2025-0088",
                SupplierName: "Fornitori Meccanici SRL",
                DocumentDate: DateTime.Today,
                Items:
                [
                    new() { ArticleCode="BRU-2000", ArticleDesc="BRUCIATORE GAS 2.0 MW",   UoM="PZ", ExpectedQty=3 },
                    new() { ArticleCode="VIT-M0820",ArticleDesc="VITE M8x20 ZN",           UoM="PZ", ExpectedQty=500 },
                ]
            ),
            new(
                DocumentRef: "DDT-2025-0091",
                SupplierName: "Elettro Components SpA",
                DocumentDate: DateTime.Today.AddDays(-1),
                Items:
                [
                    new() { ArticleCode="RES-3KW",   ArticleDesc="RESISTENZA ELETTRICA 3KW", UoM="PZ", ExpectedQty=5 },
                    new() { ArticleCode="FLT-OLE01", ArticleDesc="FILTRO OLIO MOTORE",       UoM="PZ", ExpectedQty=20 },
                ]
            ),
        ];

    }

    // ─── Autenticazione ───────────────────────────────────────────────────────

    public (bool Ok, string? Name, string? Role) TryLogin(string code, string pin)
    {
        var op = Operators.FirstOrDefault(o => o.Code == code && o.Pin == pin);
        return op == default ? (false, null, null) : (true, op.Name, op.Role);
    }

    // ─── Articoli ─────────────────────────────────────────────────────────────

    public ArticleDto? GetArticle(string code)
    {
        code = code.Trim().ToUpper();
        var art = _articles.FirstOrDefault(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (art is null) return null;

        var locations = _stocks
            .Where(s => s.PaCod == art.Code)
            .Select(s =>
            {
                var loc = _locations.FirstOrDefault(l => l.MgCod == s.MgCod && l.LcCod == s.LcCod);
                var wh = _warehouses.FirstOrDefault(w => w.Code == s.MgCod);
                return new ArticleLocationDto(
                    s.MgCod, wh?.Desc ?? s.MgCod,
                    s.LcCod, loc?.Desc ?? s.LcCod,
                    s.Qty, art.MinStock, art.MinStock * 3,
                    s.Priority, s.IsMain
                );
            })
            .OrderByDescending(l => l.IsMainWithdrawal)
            .ThenBy(l => l.Priority)
            .ToList();

        var totalStock = locations.Sum(l => l.Quantity);
        var movements = _movements
            .Where(m => m.DocumentRef.Contains(art.Code) || (m.WarehouseCode != "" && locations.Any(l => l.LocationCode == m.LocationCode && l.WarehouseCode == m.WarehouseCode)))
            .OrderByDescending(m => m.Timestamp)
            .Take(10)
            .ToList();

        // Fallback: use first 8 movements
        if (movements.Count < 3)
            movements = _movements.OrderByDescending(m => m.Timestamp).Take(8).ToList();

        return new ArticleDto(art.Code, art.Desc, art.UoM, totalStock, art.MinStock, locations, movements);
    }

    public List<ArticleDto> SearchArticles(string query)
    {
        query = query.Trim().ToUpper();
        return _articles
            .Where(a => a.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || a.Desc.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(a => GetArticle(a.Code)!)
            .Take(10)
            .ToList();
    }

    // ─── Locazioni ────────────────────────────────────────────────────────────

    public LocationDto? GetLocation(string mgcod, string lccod)
    {
        lccod = lccod.Trim().ToUpper();
        var loc = _locations.FirstOrDefault(l =>
            l.MgCod.Equals(mgcod, StringComparison.OrdinalIgnoreCase) &&
            l.LcCod.Equals(lccod, StringComparison.OrdinalIgnoreCase));
        if (loc is null)
        {
            // Try to find by location code alone
            loc = _locations.FirstOrDefault(l => l.LcCod.Equals(lccod, StringComparison.OrdinalIgnoreCase));
            if (loc is null) return null;
            mgcod = loc.MgCod;
        }

        var wh = _warehouses.FirstOrDefault(w => w.Code == loc.MgCod);
        var contents = _stocks
            .Where(s => s.MgCod == loc.MgCod && s.LcCod == loc.LcCod && s.Qty > 0)
            .Select(s =>
            {
                var art = _articles.FirstOrDefault(a => a.Code == s.PaCod);
                return new LocationContentDto(s.PaCod, art?.Desc ?? s.PaCod, art?.UoM ?? "PZ", s.Qty, s.Priority);
            })
            .OrderBy(c => c.ArticleCode)
            .ToList();

        var locMovements = _movements
            .Where(m => m.WarehouseCode == loc.MgCod && m.LocationCode == loc.LcCod)
            .OrderByDescending(m => m.Timestamp)
            .Take(8)
            .ToList();

        if (locMovements.Count < 2)
            locMovements = _movements.OrderByDescending(m => m.Timestamp).Take(5).ToList();

        return new LocationDto(
            loc.MgCod, wh?.Desc ?? loc.MgCod,
            loc.LcCod, loc.Desc,
            "Scaffale", loc.Lane, loc.HPos, loc.VPos,
            loc.IsAcceptance, loc.IsFiscal,
            contents, locMovements
        );
    }

    public List<LocationSummaryDto> GetLocations(string warehouseCode)
        => _locations
            .Where(l => l.MgCod.Equals(warehouseCode, StringComparison.OrdinalIgnoreCase))
            .Select(l => new LocationSummaryDto(l.LcCod, l.Desc, l.MgCod))
            .ToList();

    // ─── Magazzini ────────────────────────────────────────────────────────────

    public List<WarehouseDto> GetWarehouses()
        => _warehouses.Select(w => new WarehouseDto(w.Code, w.Desc, w.Type)).ToList();

    // ─── Disponibilità ────────────────────────────────────────────────────────

    public decimal GetStock(string pacod, string mgcod, string lccod)
        => _stocks.FirstOrDefault(s =>
            s.PaCod == pacod && s.MgCod == mgcod && s.LcCod == lccod)?.Qty ?? 0;

    public List<ArticleLocationDto> GetArticleLocations(string pacod)
    {
        var art = _articles.FirstOrDefault(a => a.Code == pacod);
        return _stocks
            .Where(s => s.PaCod == pacod)
            .Select(s =>
            {
                var loc = _locations.FirstOrDefault(l => l.MgCod == s.MgCod && l.LcCod == s.LcCod);
                var wh = _warehouses.FirstOrDefault(w => w.Code == s.MgCod);
                return new ArticleLocationDto(
                    s.MgCod, wh?.Desc ?? s.MgCod,
                    s.LcCod, loc?.Desc ?? s.LcCod,
                    s.Qty, art?.MinStock ?? 0, (art?.MinStock ?? 0) * 3,
                    s.Priority, s.IsMain
                );
            })
            .OrderByDescending(l => l.IsMainWithdrawal)
            .ToList();
    }

    // ─── Info articolo (caratteristiche + PDF + immagine) ────────────────────

    public Dictionary<string, string> GetArticleCharacteristics(string code) => code.ToUpper() switch
    {
        "BRU-2000" => new() { ["Potenza"] = "2.000 kW", ["Combustibile"] = "Metano / GPL", ["Peso"] = "45 kg", ["Dimensioni"] = "600×400×350 mm", ["Classe"] = "CE III" },
        "BRU-0500" => new() { ["Potenza"] = "500 kW", ["Combustibile"] = "Metano", ["Peso"] = "18 kg", ["Dimensioni"] = "380×280×240 mm" },
        "TEN-DN25" => new() { ["DN"] = "25 mm", ["Materiale"] = "PTFE + NBR", ["Press. max"] = "16 bar", ["Temp. max"] = "200 °C", ["Std."] = "EN 12266" },
        "RES-3KW"  => new() { ["Potenza"] = "3 kW", ["Tensione"] = "230 V / 400 V", ["IP"] = "IP65", ["Materiale"] = "Acciaio inox" },
        "VIT-M0820"=> new() { ["Filettatura"] = "M8", ["Lunghezza"] = "20 mm", ["Materiale"] = "Acciaio ZN", ["Classe res."] = "8.8", ["Norma"] = "DIN 933" },
        "GUA-SIL50"=> new() { ["Diam. int."] = "50 mm", ["Spessore"] = "3 mm", ["Materiale"] = "Silicone alimentare", ["Temp."] = "-60 / +200 °C" },
        "COL-DX150"=> new() { ["DN"] = "150 mm", ["N. uscite"] = "4", ["Materiale"] = "AISI 316L", ["Press. max"] = "10 bar" },
        "POM-CEN02"=> new() { ["Potenza"] = "2 HP / 1.5 kW", ["Portata"] = "6 m³/h", ["Prevalenza"] = "25 m", ["Tensione"] = "400V 50Hz" },
        _          => new() { ["Codice"] = code }
    };

    public List<string> GetArticlePdfs(string code) => code.ToUpper() switch
    {
        "BRU-2000"  => ["Scheda tecnica BRU-2000.pdf", "Certificato CE BRU-2000.pdf", "Manuale installazione.pdf", "Dichiarazione conformità.pdf"],
        "BRU-0500"  => ["Scheda tecnica BRU-0500.pdf", "Certificato CE BRU-0500.pdf"],
        "TEN-DN25"  => ["Scheda tecnica TEN-DN25.pdf", "Certificato materiali PTFE.pdf", "Disegno DN25.pdf"],
        "RES-3KW"   => ["Scheda tecnica RES-3KW.pdf", "Schema elettrico.pdf"],
        "COL-DX150" => ["Disegno tecnico COL-DX150.pdf", "Certificato collaudo.pdf", "Schema P&I.pdf"],
        "POM-CEN02" => ["Scheda tecnica pompa.pdf", "Curva caratteristica.pdf", "Manuale manutenzione.pdf"],
        _           => [$"Scheda tecnica {code}.pdf"]
    };

    // ─── Movimenti (mock — aggiorna stato in memoria) ─────────────────────────

    public (bool Ok, string Message) ExecuteSimpleMove(
        string pacod, string fromMg, string fromLc,
        string toMg, string toLc, decimal qty,
        string operatorCode)
    {
        var fromStock = _stocks.FirstOrDefault(s => s.PaCod == pacod && s.MgCod == fromMg && s.LcCod == fromLc);
        if (fromStock is null)
            return (false, $"Articolo {pacod} non trovato in {fromMg}/{fromLc}");
        if (fromStock.Qty < qty)
            return (false, $"Disponibile: {fromStock.Qty} — richiesto: {qty}");

        // Aggiorna giacenza origine
        var fromIdx = _stocks.IndexOf(fromStock);
        _stocks[fromIdx] = fromStock with { Qty = fromStock.Qty - qty };

        // Aggiorna giacenza destinazione
        var toStock = _stocks.FirstOrDefault(s => s.PaCod == pacod && s.MgCod == toMg && s.LcCod == toLc);
        if (toStock is not null)
        {
            var toIdx = _stocks.IndexOf(toStock);
            _stocks[toIdx] = toStock with { Qty = toStock.Qty + qty };
        }
        else
        {
            _stocks.Add(new MockStock(pacod, toMg, toLc, qty, "S", false));
        }

        var op = Operators.FirstOrDefault(o => o.Code == operatorCode);
        _movements.Insert(0, new MovementDto(
            DateTime.Now, "SMI", "Scarico mag. interno", -qty,
            operatorCode, op == default ? operatorCode : op.Name,
            $"SPOST-{fromLc}-{toLc}", fromMg, fromLc));
        _movements.Insert(0, new MovementDto(
            DateTime.Now, "CMI", "Carico mag. interno", qty,
            operatorCode, op == default ? operatorCode : op.Name,
            $"SPOST-{fromLc}-{toLc}", toMg, toLc));

        return (true, $"Spostamento completato: {qty} {pacod} da {fromLc} a {toLc}");
    }

    // ─── Ordini di produzione ─────────────────────────────────────────────────

    public List<ProductionOrderDto> GetProductionOrders() => [.. _productionOrders];

    public ProductionOrderDto? GetProductionOrder(string code)
        => _productionOrders.FirstOrDefault(o => o.Code == code);

    public (bool Ok, string Message) PickProductionItem(
        string orderCode, Guid itemId, decimal qty, string operatorCode)
    {
        var order = _productionOrders.FirstOrDefault(o => o.Code == orderCode);
        if (order is null) return (false, "Ordine non trovato");

        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return (false, "Articolo non trovato");

        var fromStock = _stocks.FirstOrDefault(s =>
            s.PaCod == item.ArticleCode && s.MgCod == item.SuggestedWarehouse && s.LcCod == item.SuggestedLocation);

        if (fromStock is not null && fromStock.Qty >= qty)
        {
            var idx = _stocks.IndexOf(fromStock);
            _stocks[idx] = fromStock with { Qty = fromStock.Qty - qty };
        }

        var newPickedQty = item.PickedQty + qty;
        var newStatus = newPickedQty >= item.PlannedQty ? PickStatus.Completed :
                        newPickedQty > 0 ? PickStatus.Partial : PickStatus.Pending;

        var itemIdx = order.Items.IndexOf(item);
        order.Items[itemIdx] = item with { PickedQty = newPickedQty, Status = newStatus };

        var op = Operators.FirstOrDefault(o => o.Code == operatorCode);
        _movements.Insert(0, new MovementDto(
            DateTime.Now, "SCAR", "Scarico produzione", -qty,
            operatorCode, op == default ? operatorCode : op.Name,
            orderCode, item.SuggestedWarehouse, item.SuggestedLocation));

        return (true, $"{qty} {item.ArticleCode} prelevati per {orderCode}");
    }


    // ─── Liste di prelievo v2 ────────────────────────────────────────────────

    public List<PickListV2Dto> GetPickListsV2() => [.. _pickListsV2];

    public PickListV2Dto? GetPickListV2(string code)
        => _pickListsV2.FirstOrDefault(l => l.Code == code);

    public (bool Ok, string Message) PickListV2Item(
        string listCode, Guid itemId, decimal qty, string locationCode, string operatorCode)
    {
        var list = _pickListsV2.FirstOrDefault(l => l.Code == listCode);
        if (list is null) return (false, "Lista non trovata");
        var item = list.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return (false, "Articolo non trovato");

        // Scala giacenza
        var stock = _stocks.FirstOrDefault(s => s.PaCod == item.ArticleCode && s.LcCod == locationCode);
        if (stock is not null)
        {
            var idx = _stocks.IndexOf(stock);
            _stocks[idx] = stock with { Qty = Math.Max(0, stock.Qty - qty) };
            // Ricalcola BestStock
            item.BestStockQty = Math.Max(0, item.BestStockQty - qty);
        }

        item.PickedQty = Math.Min(item.PlannedQty, item.PickedQty + qty);
        item.Status = item.PickedQty >= item.PlannedQty ? PickStatus.Completed
                    : item.PickedQty > 0                ? PickStatus.Partial
                    : PickStatus.Pending;

        var op = Operators.FirstOrDefault(o => o.Code == operatorCode);
        _movements.Insert(0, new MovementDto(DateTime.Now, "SMI", "Scarico lista prelievo", -qty,
            operatorCode, op == default ? operatorCode : op.Name, listCode, "MAG", locationCode));

        return (true, $"{qty} {item.ArticleCode} prelevati");
    }

    // ─── Accettazione ─────────────────────────────────────────────────────────

    public List<AcceptanceDocDto> GetAcceptanceDocs() => [.. _acceptanceDocs];

    public AcceptanceDocDto? GetAcceptanceDoc(string docRef)
        => _acceptanceDocs.FirstOrDefault(d =>
            d.DocumentRef.Equals(docRef.Trim(), StringComparison.OrdinalIgnoreCase));

    public AcceptanceDocDto? FindDocByArticle(string articleCode)
        => _acceptanceDocs.FirstOrDefault(d =>
            d.Items.Any(i => i.ArticleCode.Equals(articleCode.Trim(), StringComparison.OrdinalIgnoreCase)
                          && i.Status != AcceptanceStatus.Done));

    public (bool Ok, string Message) ExecuteAcceptance(
        string docRef, Guid itemId, decimal qty, string destLocation, string operatorCode)
    {
        var doc = _acceptanceDocs.FirstOrDefault(d => d.DocumentRef == docRef);
        if (doc is null) return (false, "Documento non trovato");
        var item = doc.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return (false, "Articolo non trovato");

        item.AcceptedQty = Math.Min(item.ExpectedQty, item.AcceptedQty + qty);
        item.DestLocation = destLocation;
        item.Status = item.AcceptedQty >= item.ExpectedQty ? AcceptanceStatus.Done
                    : item.AcceptedQty > 0                 ? AcceptanceStatus.Partial
                    : AcceptanceStatus.Pending;

        // Carico in magazzino
        var existingStock = _stocks.FirstOrDefault(s => s.PaCod == item.ArticleCode && s.LcCod == destLocation);
        if (existingStock is not null)
        {
            var idx = _stocks.IndexOf(existingStock);
            _stocks[idx] = existingStock with { Qty = existingStock.Qty + qty };
        }
        else
        {
            _stocks.Add(new MockStock(item.ArticleCode, "MAG", destLocation, qty, "P", true));
        }

        var op = Operators.FirstOrDefault(o => o.Code == operatorCode);
        _movements.Insert(0, new MovementDto(DateTime.Now, "ACQ", "Carico per acquisto", qty,
            operatorCode, op == default ? operatorCode : op.Name, docRef, "MAG", destLocation));

        return (true, $"{qty} {item.ArticleCode} accettati in {destLocation}");
    }

    // ─── KPI Dashboard ────────────────────────────────────────────────────────

    public int GetMovementsToday()
        => _movements.Count(m => m.Timestamp.Date == DateTime.Today);

    public int GetOpenProductionOrders()
        => _productionOrders.Count(o => o.Status != "Chiuso");

    public int GetOpenPickLists()
        => _pickListsV2.Count(l => l.Status != "Chiusa");

    public int GetLowStockArticles()
        => _articles.Count(a =>
        {
            var total = _stocks.Where(s => s.PaCod == a.Code).Sum(s => s.Qty);
            return total <= a.MinStock;
        });

    // ─── Movimenti fittizi iniziali ───────────────────────────────────────────

    private static List<MovementDto> GenerateMockMovements()
    {
        var now = DateTime.Now;
        return
        [
            new(now.AddHours(-0.5), "CMI", "Carico mag. interno", 10, "OP01", "Marco Rossi", "SPOST-TRANS-01-A-01-01", "MAG", "A-01-01"),
            new(now.AddHours(-1), "SMI", "Scarico mag. interno", -10, "OP01", "Marco Rossi", "SPOST-TRANS-01-A-01-01", "TRANS", "TRANS-01"),
            new(now.AddHours(-2), "SCAR", "Scarico produzione", -5, "OP02", "Luigi Bianchi", "OP-2025-0340", "MAG", "C-01-03"),
            new(now.AddHours(-3), "ACQ", "Carico per acquisto", 50, "OP01", "Marco Rossi", "DOC-2025-0215", "TRANS", "TRANS-01"),
            new(now.AddDays(-1).AddHours(-1), "SCAR", "Scarico produzione", -8, "OP02", "Luigi Bianchi", "OP-2025-0338", "MAG", "A-01-01"),
            new(now.AddDays(-1).AddHours(-3), "CMI", "Carico mag. interno", 8, "SUP01", "Maria Verdi", "SPOST-B-A", "MAG", "A-01-01"),
            new(now.AddDays(-1).AddHours(-4), "SMI", "Scarico mag. interno", -8, "SUP01", "Maria Verdi", "SPOST-B-A", "MAG", "B-02-01"),
            new(now.AddDays(-2), "ACQ", "Carico per acquisto", 100, "OP01", "Marco Rossi", "DOC-2025-0210", "TRANS", "TRANS-01"),
            new(now.AddDays(-2).AddHours(-2), "SCAR", "Scarico produzione", -3, "OP02", "Luigi Bianchi", "OP-2025-0335", "MAG", "C-02-01"),
            new(now.AddDays(-3), "REP", "Rettifica positiva", 2, "SUP01", "Maria Verdi", "INV-2025-001", "MAG", "BULK-01"),
        ];
    }
}
