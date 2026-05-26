const fs = require('fs');
const path = require('path');

const outDir = 'C:\\dev\\alpla-portal\\docs\\test-data';
if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
}

// 1. Suppliers (25)
const supplierCategories = [
    'Comércio Geral', 'Manutenção Industrial', 'Material Elétrico', 'Peças Mecânicas',
    'Material de Escritório', 'Equipamento TI', 'EPI / Segurança', 'Transporte e Logística',
    'Limpeza e Higiene', 'Construção Civil', 'Gráfica e Papelaria', 'Embalagens'
];

const suppliers = [];
for (let i = 1; i <= 25; i++) {
    const category = supplierCategories[i % supplierCategories.length];
    suppliers.push({
        Id: `SUP-${i.toString().padStart(3, '0')}`,
        Name: `Empresa Exemplo ${i} - ${category} (SU) LDA`,
        NIF: `541${Math.floor(1000000 + Math.random() * 9000000)}`,
        Address: `Rua Principal, Polo Industrial de Viana, Luanda`,
        ContactPerson: `Contacto ${i}`,
        Email: `contato${i}@empresa${i}.co.ao`,
        Phone: `+244 923 ${Math.floor(100000 + Math.random() * 900000)}`,
        Category: category,
        PaymentTerms: i % 3 === 0 ? '30 dias' : (i % 2 === 0 ? 'Pronto Pagamento' : '15 dias'),
        CurrencyPreference: i % 4 === 0 ? 'USD' : (i % 5 === 0 ? 'EUR' : 'AOA'),
        Notes: `Fornecedor estratégico de ${category}`
    });
}

const toCsv = (arr) => {
    if (!arr.length) return '';
    const keys = Object.keys(arr[0]);
    const header = keys.join(';');
    const rows = arr.map(obj => keys.map(k => `"${(obj[k] || '').toString().replace(/"/g, '""')}"`).join(';'));
    return [header, ...rows].join('\n');
};

fs.writeFileSync(path.join(outDir, '01_Suppliers.csv'), toCsv(suppliers));

// 2. Catalog Items (150)
const items = [];
const itemTypes = {
    'Manutenção Industrial': ['Rolamento', 'Correia', 'Mangueira Industrial', 'Válvula', 'Filtro'],
    'Material Elétrico': ['Cabo Elétrico', 'Contactor', 'Disjuntor', 'Sensor', 'Lâmpada LED'],
    'EPI / Segurança': ['Luvas de Proteção', 'Capacete', 'Botas de Segurança', 'Óculos', 'Colete'],
    'Material de Escritório': ['Resma de Papel A4', 'Canetas', 'Dossier', 'Agrafador', 'Bloco de Notas'],
    'Equipamento TI': ['Teclado', 'Rato', 'Monitor', 'Cabo de Rede', 'Tinteiro'],
    'Limpeza e Higiene': ['Detergente', 'Lixívia', 'Esfregão', 'Sacos de Lixo', 'Sabonete Líquido'],
    'Peças Mecânicas': ['Mola', 'Parafuso', 'Porca', 'Eixo', 'Engrenagem'],
    'Embalagens': ['Caixa de Cartão', 'Fita Adesiva', 'Rolo de Filme', 'Palete', 'Etiquetas'],
    'Construção Civil': ['Cimento', 'Tinta', 'Pincel', 'Tijolo', 'Tubo PVC'],
    'Gráfica e Papelaria': ['Cartões de Visita', 'Envelopes', 'Carimbo', 'Caderno', 'Pasta'],
    'Transporte e Logística': ['Frete Local', 'Aluguer de Empilhador', 'Serviço de Carga', 'Transporte de Pessoal', 'Estafeta'],
    'Comércio Geral': ['Água Mineral', 'Café', 'Açúcar', 'Copo Plástico', 'Guardanapos']
};

for (let i = 1; i <= 150; i++) {
    const supplier = suppliers[i % suppliers.length];
    const categoryItems = itemTypes[supplier.Category] || itemTypes['Comércio Geral'];
    const baseName = categoryItems[i % categoryItems.length];
    const price = Math.floor(Math.random() * 50000) + 1000;
    
    items.push({
        ItemCode: `ITM-${i.toString().padStart(4, '0')}`,
        Description: `${baseName} Modelo X-${i}`,
        Unit: i % 10 === 0 ? 'CX' : 'UN',
        SupplierId: supplier.Id,
        Supplier: supplier.Name,
        ReferencePrice: price,
        Currency: supplier.CurrencyPreference,
        PaymentTerms: supplier.PaymentTerms,
        IVA_Percentage: i % 4 === 0 ? 0 : 14,
        Discount_Percentage: i % 15 === 0 ? 10 : 0,
        Notes: `Uso contínuo na fábrica`
    });
}
fs.writeFileSync(path.join(outDir, '02_CatalogItems.csv'), toCsv(items));


// --- NEW: 06. Catalog Item Price History ---
const priceHistory = [];
let phId = 1;

// We need history for at least 80 items. Let's do 90.
// For at least 30, create 3 or more historical records.
const historyReasons = [
    'Stable price over time', 'Small price increase', 'Large price increase', 'Small price decrease',
    'Promotional discount', 'Supplier renegotiation', 'Currency-related variation', 'Price correction due to previous wrong entry',
    'IVA change impact', 'Urgent purchase surcharge', 'Contracted price versus one-off purchase'
];

for(let i=0; i<90; i++) {
    let item = items[i];
    let numRecords = i < 35 ? (Math.floor(Math.random() * 3) + 3) : (Math.floor(Math.random() * 2) + 1);
    
    let currentPrice = item.ReferencePrice * 0.7; // start low
    let currentDate = new Date('2024-01-15');

    for(let r=0; r<numRecords; r++) {
        let isLast = r === numRecords - 1;
        let nextPrice = isLast ? item.ReferencePrice : currentPrice * (1 + ((Math.random()*0.3)-0.05));
        if (nextPrice < 0) nextPrice = currentPrice;
        
        let reason = historyReasons[Math.floor(Math.random() * historyReasons.length)];
        if (isLast && reason.includes('correction')) {
            nextPrice = item.ReferencePrice; // enforce final match
        }

        let effectiveTo = new Date(currentDate);
        effectiveTo.setMonth(effectiveTo.getMonth() + 6 + Math.floor(Math.random()*4));
        if (isLast) effectiveTo = new Date('2026-12-31');

        priceHistory.push({
            PriceHistoryId: `PH-${phId.toString().padStart(4, '0')}`,
            ItemCode: item.ItemCode,
            ItemDescription: item.Description,
            SupplierId: item.SupplierId,
            SupplierName: item.Supplier,
            Currency: item.Currency,
            PaymentTerms: item.PaymentTerms,
            PurchaseContext: r % 2 === 0 ? 'Contracted' : 'One-off',
            PreviousUnitPrice: currentPrice.toFixed(2),
            NewUnitPrice: nextPrice.toFixed(2),
            PriceVariationValue: (nextPrice - currentPrice).toFixed(2),
            PriceVariationPercentage: (((nextPrice - currentPrice) / currentPrice) * 100).toFixed(2) + '%',
            IVA: item.IVA_Percentage,
            DiscountPercentage: item.Discount_Percentage,
            GlobalDiscountPercentage: 0,
            EffectiveFromDate: currentDate.toISOString().split('T')[0],
            EffectiveToDate: effectiveTo.toISOString().split('T')[0],
            SourceDocumentType: 'P.O',
            SourceDocumentNumber: `PO-${2024+Math.floor(r/2)}-${100+r}`,
            SourceScenarioId: `SCN-HIST-${i}-${r}`,
            ChangeReason: reason,
            PriceHistoryKey: `${item.ItemCode}|${item.SupplierId}|${item.Currency}`,
            Notes: 'Histórico gerado automaticamente'
        });

        currentPrice = nextPrice;
        currentDate = new Date(effectiveTo);
        currentDate.setDate(currentDate.getDate() + 1);
        phId++;
    }
}
fs.writeFileSync(path.join(outDir, '06_CatalogItemPriceHistory.csv'), toCsv(priceHistory));


// 3. Scenarios (50)
const scenarios = [];
const inconsistencies = [];

const scenarioTypes = [
    { Type: 'Payment', Desc: 'Simple payment request with one item', Edges: 'None', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Payment request with multiple items', Edges: 'None', PriceHist: 'None' },
    { Type: 'Quotation', Desc: 'Quotation request without supplier initially', Edges: 'None', PriceHist: 'None' },
    { Type: 'Quotation', Desc: 'Quotation request later converted to selected supplier', Edges: 'None', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with item-level discount', Edges: 'Item Discount', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with global discount', Edges: 'Global Discount', PriceHist: 'None' },
    { Type: 'Quotation', Desc: 'Request with both item-level discount and global discount', Edges: 'Mixed Discounts', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with no discount', Edges: 'No Discount', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with IVA 14%', Edges: 'IVA 14', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with IVA 0%', Edges: 'IVA 0', PriceHist: 'None' },
    { Type: 'Quotation', Desc: 'Request with mixed IVA rates', Edges: 'Mixed IVA', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with AOA', Edges: 'Currency AOA', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with USD', Edges: 'Currency USD', PriceHist: 'None' },
    { Type: 'Quotation', Desc: 'Request with EUR', Edges: 'Currency EUR', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with partial receiving', Edges: 'Partial Receiving', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with full receiving', Edges: 'Full Receiving', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with scheduled payment', Edges: 'Scheduled Payment', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with paid amount equal to approved amount', Edges: 'Exact Payment', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with paid amount slightly different but within tolerance', Edges: 'Tolerance Payment', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with paid amount above tolerance to test payment divergence', Edges: 'Divergence Payment', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with missing/incorrect document data for validation testing', Edges: 'Missing Doc Data', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with supplier NIF formatting inconsistencies', Edges: 'NIF Inconsistency', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with repeated supplier names but different NIFs', Edges: 'Duplicate Name', PriceHist: 'None' },
    { Type: 'Payment', Desc: 'Request with same NIF but slightly different supplier name', Edges: 'Duplicate NIF', PriceHist: 'None' }
];

// Add price history specific scenarios
const priceHistoryTests = [
    { Desc: 'Proforma uses old price, catalog has newer price', PriceHist: 'Proforma uses an old price, but the catalog has a newer price' },
    { Desc: 'P.O uses latest approved price', PriceHist: 'The P.O uses the latest approved price' },
    { Desc: 'Payment Proof matches P.O not Proforma', PriceHist: 'The Payment Proof matches the P.O, not the Proforma' },
    { Desc: 'Receipt uses only quantity and total', PriceHist: 'The Receipt uses only quantity and total, requiring reconciliation' },
    { Desc: 'Price changed between quote and PO', PriceHist: 'The item price changed between quotation and purchase approval' },
    { Desc: 'Price above catalog, needs justification', PriceHist: 'The item price is above the latest catalog price and should require justification' },
    { Desc: 'Price below catalog due to discount', PriceHist: 'The item price is below the latest catalog price due to negotiated discount' },
    { Desc: 'Lower price from another supplier', PriceHist: 'The same item exists from another supplier at a lower price' },
    { Desc: 'Same item another currency', PriceHist: 'The same item exists in another currency and should not be directly compared without conversion logic' },
    { Desc: 'Same supplier different payment terms', PriceHist: 'The same item and supplier have different historical prices based on payment terms' },
    { Desc: 'Display price variation against last purchase', PriceHist: 'The system should display price variation compared with the last purchase for the same ItemCode + SupplierId + Currency' },
    { Desc: 'Warning on abnormal price increase', PriceHist: 'The system should warn when the new price is significantly higher than the historical average' },
    { Desc: 'No cross-supplier direct comparison', PriceHist: 'The system should not compare prices between different suppliers as if they were the same price history chain' },
    { Desc: 'No cross-currency direct comparison', PriceHist: 'The system should not compare prices between different currencies without explicit currency conversion logic' },
    { Desc: 'Distinguish discount from unit price change', PriceHist: 'The system should distinguish item-level discount from actual unit price change' }
];

// Combine scenarios to ensure 50 total.
const finalScenarioTypes = [...scenarioTypes];
for(let pht of priceHistoryTests) {
    finalScenarioTypes.push({ Type: 'Quotation', Desc: pht.Desc, Edges: 'Price History', PriceHist: pht.PriceHist });
}
while(finalScenarioTypes.length < 50) {
    finalScenarioTypes.push({ Type: 'Payment', Desc: 'Additional standard payment', Edges: 'None', PriceHist: 'None' });
}

for (let i = 1; i <= 50; i++) {
    const st = finalScenarioTypes[i - 1];
    
    // Select an item that has price history
    const item = items[(i - 1) % 90]; 
    const supplier = suppliers.find(s => s.Id === item.SupplierId);
    
    const qty = Math.floor(Math.random() * 10) + 1;
    let price = item.ReferencePrice;
    
    let scenarioPrice = price;
    let diffValue = 0;
    let requiresJustification = 'No';
    let suggestedWarning = '';

    if (st.PriceHist.includes('above the latest catalog price')) {
        scenarioPrice = price * 1.35; // 35% higher
        diffValue = scenarioPrice - price;
        requiresJustification = 'Yes';
        suggestedWarning = 'Warning: New price is significantly higher than the historical average.';
    } else if (st.PriceHist.includes('below the latest catalog price due to negotiated discount')) {
        scenarioPrice = price * 0.85; // 15% lower
        diffValue = scenarioPrice - price;
    } else if (st.PriceHist.includes('old price')) {
        scenarioPrice = price * 0.70;
        diffValue = scenarioPrice - price;
    }

    const ivaPct = st.Edges.includes('IVA 0') ? 0 : item.IVA_Percentage;
    const itemDisc = st.Edges.includes('Item Discount') || st.Edges.includes('Mixed Discounts') ? (scenarioPrice * 0.1) : 0;
    const subtotal = (scenarioPrice - itemDisc) * qty;
    const globalDisc = st.Edges.includes('Global Discount') || st.Edges.includes('Mixed Discounts') ? (subtotal * 0.05) : 0;
    const baseTotal = subtotal - globalDisc;
    const ivaTotal = baseTotal * (ivaPct / 100);
    const grandTotal = baseTotal + ivaTotal;

    scenarios.push({
        ScenarioId: `SCN-${i.toString().padStart(3, '0')}`,
        Title: `Requisição de Teste ${i} - ${st.Desc}`,
        Type: st.Type,
        Supplier: supplier.Name,
        Department: i % 2 === 0 ? 'Manutenção' : 'Administração',
        Plant: 'Luanda',
        CostCenter: `CC-${(i % 5) + 1}00`,
        ItemsDescription: `${qty}x ${item.Description}`,
        Quantity: qty,
        UnitPrice: scenarioPrice.toFixed(2),
        IVA_Percentage: ivaPct,
        ItemDiscount: itemDisc.toFixed(2),
        GlobalDiscount: globalDisc.toFixed(2),
        ExpectedSubtotal: subtotal.toFixed(2),
        ExpectedIVA: ivaTotal.toFixed(2),
        ExpectedDiscountTotal: ((itemDisc * qty) + globalDisc).toFixed(2),
        ExpectedGrandTotal: grandTotal.toFixed(2),
        SuggestedDueDate: '2026-05-10',
        SuggestedJustification: `Necessidade para testar: ${st.Desc}`,
        EdgeCase: st.Edges,
        PriceHistoryTest: st.PriceHist !== 'None' ? 'Yes' : 'No',
        PriceHistoryExpectedBehavior: st.PriceHist,
        PriceHistoryKey: `${item.ItemCode}|${item.SupplierId}|${item.Currency}`,
        CatalogReferencePrice: item.ReferencePrice.toFixed(2),
        LastPurchasePrice: price.toFixed(2),
        HistoricalAveragePrice: (price * 0.9).toFixed(2),
        ScenarioUnitPrice: scenarioPrice.toFixed(2),
        PriceDifferenceValue: diffValue.toFixed(2),
        PriceDifferencePercentage: (((scenarioPrice - price) / price) * 100).toFixed(2) + '%',
        SupplierComparisonAvailable: st.PriceHist.includes('another supplier') ? 'Yes' : 'No',
        AlternativeSupplierLowerPrice: st.PriceHist.includes('another supplier') ? (price * 0.8).toFixed(2) : '',
        CurrencyComparisonRequired: st.PriceHist.includes('another currency') ? 'Yes' : 'No',
        PaymentTermsImpact: st.PriceHist.includes('payment terms') ? 'Yes' : 'No',
        RequiresPriceJustification: requiresJustification,
        SuggestedSystemWarning: suggestedWarning
    });
}
fs.writeFileSync(path.join(outDir, '03_Scenarios.csv'), toCsv(scenarios));

// 4. Document Matrix & 5. Inconsistencies
const docs = [];

scenarios.forEach((scn, idx) => {
    const reqDocs = ['Proforma', 'P.O', 'Comprovativo de Agendamento', 'Comprovativo de Pagamento', 'Recibo'];
    
    let docNif = scn.Supplier.includes('NIF') ? '123456789' : suppliers.find(s => s.Name === scn.Supplier)?.NIF || '000000000';
    let docName = scn.Supplier;
    let finalAmount = parseFloat(scn.ExpectedGrandTotal);
    
    let inconsistencyDesc = '';
    
    if (scn.EdgeCase === 'NIF Inconsistency') {
        docNif = docNif.substring(0, 3) + ' ' + docNif.substring(3, 6) + ' ' + docNif.substring(6);
        inconsistencyDesc = 'NIF com espaços na Proforma';
    } else if (scn.EdgeCase === 'Tolerance Payment') {
        finalAmount = finalAmount + 0.50;
        inconsistencyDesc = 'Pagamento com diferença de 0.50 AOA (dentro da tolerância)';
    } else if (scn.EdgeCase === 'Divergence Payment') {
        finalAmount = finalAmount + 5000;
        inconsistencyDesc = 'Pagamento com diferença de 5000 AOA (fora da tolerância, gera divergência)';
    } else if (scn.EdgeCase === 'Missing Doc Data') {
        docNif = '';
        inconsistencyDesc = 'NIF ausente no Recibo';
    } else if (scn.EdgeCase === 'Mixed Discounts') {
        inconsistencyDesc = 'Resumo do documento soma os descontos de linha com o desconto global de forma separada.';
    } else if (scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('Proforma uses an old price')) {
        inconsistencyDesc = 'Proforma price older than catalog price';
    } else if (scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('Payment Proof matches the P.O, not the Proforma')) {
        inconsistencyDesc = 'P.O price different from Proforma price; Payment proof aligned with P.O';
    } else if (scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('Receipt uses only quantity and total')) {
        inconsistencyDesc = 'Receipt total missing unit price';
    } else if (scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('another supplier')) {
        inconsistencyDesc = 'Same item cheaper from another supplier';
    } else if (scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('another currency')) {
        inconsistencyDesc = 'Same item in different currency incorrectly compared as direct price variation';
    }

    let currentScenarioTime = new Date(2026, 2, 26).getTime() + Math.floor(Math.random() * 10 * 86400000); // Start between 26/03/2026 and 05/04/2026

    reqDocs.forEach(dtype => {
        // Increment date by 1 to 5 days
        currentScenarioTime += Math.floor(Math.random() * 5 + 1) * 86400000;
        let maxTime = new Date(2026, 3, 26).getTime(); // 26/04/2026
        if (currentScenarioTime > maxTime) currentScenarioTime = maxTime;
        let dObj = new Date(currentScenarioTime);
        let dStr = `${dObj.getFullYear()}-${(dObj.getMonth()+1).toString().padStart(2, '0')}-${dObj.getDate().toString().padStart(2, '0')}`;

        let amount = finalAmount;
        if (dtype === 'Comprovativo de Pagamento' && scn.EdgeCase === 'Tolerance Payment') amount = finalAmount;
        if (dtype === 'Comprovativo de Pagamento' && scn.EdgeCase === 'Divergence Payment') amount = finalAmount;
        if (dtype === 'Proforma' && scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('Proforma uses an old price')) {
            amount = amount * 0.7; // simulate old price total
        }
        
        docs.push({
            ScenarioId: scn.ScenarioId,
            DocumentType: dtype,
            DocumentNumber: `${dtype.substring(0,3).toUpperCase()}-2026-${idx+1}`,
            DocumentDate: dStr,
            SupplierName: docName,
            SupplierNIF: docNif,
            TotalAmount: amount.toFixed(2),
            PaymentStatus: dtype === 'Comprovativo de Pagamento' ? 'Pago' : 'Pendente',
            PriceHistoryKey: scn.PriceHistoryKey,
            PriceSourceUsed: dtype === 'Proforma' ? 'Quotation' : 'Catalog',
            PriceMatchesCatalog: dtype === 'Proforma' && scn.PriceHistoryExpectedBehavior && scn.PriceHistoryExpectedBehavior.includes('old price') ? 'No' : 'Yes',
            PriceMatchesLastPurchase: 'Yes',
            PriceRequiresReview: scn.RequiresPriceJustification,
            PriceReviewReason: scn.SuggestedSystemWarning,
            PaymentTermsUsed: scn.PaymentTermsImpact === 'Yes' ? 'Pronto Pagamento' : '30 dias',
            PurchaseContextUsed: 'One-off'
        });
    });

    if (inconsistencyDesc) {
        inconsistencies.push({
            ScenarioId: scn.ScenarioId,
            EdgeCase: scn.EdgeCase || 'Price History',
            Description: inconsistencyDesc,
            TestObjective: `Validar se o sistema reage corretamente à inconsistência: ${inconsistencyDesc}`
        });
    }
});

fs.writeFileSync(path.join(outDir, '04_DocumentMatrix.csv'), toCsv(docs));
fs.writeFileSync(path.join(outDir, '05_Inconsistencies.csv'), toCsv(inconsistencies));

console.log('Dataset extended with Price History at', outDir);
