const fs = require('fs');
const path = require('path');
const PDFDocument = require('pdfkit');
const { parse } = require('csv-parse/sync');

const dataDir = 'C:\\dev\\alpla-portal\\docs\\test-data';
const outDir = path.join(dataDir, 'pdfs');

if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
}

const loadCsv = (filename) => {
    const filePath = path.join(dataDir, filename);
    if (!fs.existsSync(filePath)) return [];
    const content = fs.readFileSync(filePath, 'utf8');
    return parse(content, { columns: true, skip_empty_lines: true, delimiter: ';' });
};

const suppliers = loadCsv('01_Suppliers.csv');
const catalogItems = loadCsv('02_CatalogItems.csv');
const scenarios = loadCsv('03_Scenarios.csv');
const docMatrix = loadCsv('04_DocumentMatrix.csv');
const inconsistencies = loadCsv('05_Inconsistencies.csv');

// Helpers
const formatCurrency = (val, currency = 'AOA') => {
    return `${parseFloat(val).toLocaleString('pt-AO', { minimumFractionDigits: 2 })} ${currency}`;
};

const numberToWords = (num) => {
    // Very simplified generic text for demo. OCR handles amounts, the exact written number acts as noise/context.
    const integerPart = Math.floor(parseFloat(num));
    return `(Extenso do valor: ${integerPart} e cêntimos)`;
};

const getSupplier = (nameOrId) => {
    return suppliers.find(s => s.Id === nameOrId || s.Name === nameOrId) || suppliers[0];
};

const applyInconsistencies = (scenarioId, docType, data) => {
    const inc = inconsistencies.find(i => i.ScenarioId === scenarioId && i.TargetDocument === docType);
    if (!inc) return data;
    
    let modified = { ...data };
    if (inc.InconsistencyType === 'Missing NIF') modified.SupplierNIF = '';
    if (inc.InconsistencyType === 'Wrong Total') modified.TotalAmount = (parseFloat(data.TotalAmount) * 1.1).toFixed(2);
    if (inc.InconsistencyType === 'Mismatched Supplier Name') modified.SupplierName = data.SupplierName + ' Lda';
    if (inc.InconsistencyType === 'Missing IVA') modified.IVA_Percentage = '0';
    
    return modified;
};

// Drawing Functions
const drawHeader = (doc, title, supplier, docData) => {
    doc.fontSize(20).text(title, { align: 'center' }).moveDown();
    
    doc.fontSize(12)
       .text(`Fornecedor: ${docData.SupplierName || supplier.Name}`)
       .text(`NIF: ${docData.SupplierNIF || supplier.NIF}`)
       .text(`Endereço: ${supplier.Address}`)
       .text(`Contacto: ${supplier.ContactPerson} | ${supplier.Phone}`)
       .moveDown();
       
    doc.text(`Nº Documento: ${docData.DocumentNumber}`)
       .text(`Data: ${docData.DocumentDate}`)
       .moveDown();
};

const drawItems = (doc, scenario, isReceipt = false) => {
    const curr = scenario.CurrencyComparisonRequired === 'Yes' ? 'USD' : 'AOA'; // simplification
    doc.fontSize(14).text('Itens', { underline: true }).moveDown();
    
    doc.fontSize(10);
    // Simple table
    const startY = doc.y;
    doc.text('Descrição', 50, startY);
    doc.text('Qtd', 300, startY);
    if (!isReceipt) doc.text('Preço Unit.', 350, startY);
    doc.text('Total', 450, startY);
    
    doc.moveTo(50, startY + 15).lineTo(550, startY + 15).stroke();
    
    let y = startY + 20;
    const desc = scenario.ItemsDescription;
    const qty = scenario.Quantity;
    const unitPrice = scenario.UnitPrice;
    const total = parseFloat(qty) * parseFloat(unitPrice);
    
    doc.text(desc, 50, y);
    doc.text(qty, 300, y);
    if (!isReceipt) doc.text(formatCurrency(unitPrice, ''), 350, y);
    doc.text(formatCurrency(total, ''), 450, y);
    
    y += 20;
    doc.moveTo(50, y).lineTo(550, y).stroke();
    doc.y = y + 10;
};

const drawTotals = (doc, scenario, docData, isReceipt = false) => {
    const curr = scenario.CurrencyComparisonRequired === 'Yes' ? 'USD' : 'AOA';
    doc.fontSize(10);
    
    if (!isReceipt) {
        doc.text(`Subtotal: ${formatCurrency(scenario.ExpectedSubtotal, curr)}`, { align: 'right' });
        doc.text(`Desconto: ${formatCurrency(scenario.ExpectedDiscountTotal, curr)}`, { align: 'right' });
        doc.text(`IVA (${scenario.IVA_Percentage}%): ${formatCurrency(scenario.ExpectedIVA, curr)}`, { align: 'right' });
    }
    
    doc.fontSize(12).font('Helvetica-Bold')
       .text(`TOTAL: ${formatCurrency(docData.TotalAmount, curr)}`, { align: 'right' })
       .font('Helvetica');
       
    doc.moveDown();
    doc.fontSize(10).font('Helvetica-Oblique')
       .text(`Extenso: ${numberToWords(docData.TotalAmount)}`, { align: 'right' })
       .font('Helvetica');
};

const createProforma = (doc, scenario, docData, supplier) => {
    drawHeader(doc, 'FACTURA PROFORMA', supplier, docData);
    drawItems(doc, scenario);
    drawTotals(doc, scenario, docData);
};

const createPO = (doc, scenario, docData, supplier) => {
    // Alpla style
    doc.rect(0, 0, 612, 50).fill('#003B7E');
    doc.fillColor('#FFFFFF').fontSize(24).text('ALPLA PURCHASE ORDER', 50, 15).fillColor('#000000');
    doc.y = 70;
    
    doc.fontSize(12)
       .text(`Para: ${docData.SupplierName || supplier.Name}`)
       .text(`NIF: ${docData.SupplierNIF || supplier.NIF}`)
       .text(`Data: ${docData.DocumentDate}`)
       .text(`P.O. Nº: ${docData.DocumentNumber}`)
       .moveDown();
       
    drawItems(doc, scenario);
    drawTotals(doc, scenario, docData);
};

const createSchedulingProof = (doc, scenario, docData, supplier) => {
    doc.fontSize(20).text('COMPROVATIVO DE AGENDAMENTO', { align: 'center' }).moveDown();
    doc.fontSize(12)
       .text(`Data do Agendamento: ${docData.DocumentDate}`)
       .text(`Beneficiário: ${docData.SupplierName || supplier.Name}`)
       .text(`NIF Beneficiário: ${docData.SupplierNIF || supplier.NIF}`)
       .text(`Referência P.O: SCN-${scenario.ScenarioId}`)
       .moveDown();
       
    doc.fontSize(14).text(`Valor Agendado: ${formatCurrency(docData.TotalAmount, 'AOA')}`);
};

const createPaymentProof = (doc, scenario, docData, supplier) => {
    doc.rect(50, 50, 512, 100).stroke();
    doc.fontSize(20).text('COMPROVATIVO DE TRANSFERÊNCIA', 60, 60);
    doc.fontSize(12).text('Banco Angolano de Testes - Operação Sucesso', 60, 90);
    
    doc.y = 170;
    doc.text(`Data da Operação: ${docData.DocumentDate}`)
       .text(`Ordenante: ALPLA ANGOLA`)
       .text(`Beneficiário: ${docData.SupplierName || supplier.Name}`)
       .text(`NIF Beneficiário: ${docData.SupplierNIF || supplier.NIF}`)
       .moveDown();
       
    doc.fontSize(16).text(`Montante Transferido: ${formatCurrency(docData.TotalAmount, 'AOA')}`);
    doc.fontSize(10).font('Helvetica-Oblique').text(`(${numberToWords(docData.TotalAmount)})`);
};

const createReceipt = (doc, scenario, docData, supplier) => {
    doc.fontSize(20).text('RECIBO', { align: 'center' }).moveDown();
    drawHeader(doc, '', supplier, docData);
    
    doc.text(`Recebemos de ALPLA ANGOLA a quantia de ${formatCurrency(docData.TotalAmount, 'AOA')} referente à liquidação da fatura associada à P.O SCN-${scenario.ScenarioId}.`).moveDown();
    
    drawItems(doc, scenario, true); // true = missing unit price as requested
    drawTotals(doc, scenario, docData, true);
};

// Generate Loop
let generatedCount = 0;
const sampleNames = [];

for (const scenario of scenarios) {
    const sId = scenario.ScenarioId;
    const sDir = path.join(outDir, sId);
    if (!fs.existsSync(sDir)) fs.mkdirSync(sDir, { recursive: true });
    
    const docsForScenario = docMatrix.filter(d => d.ScenarioId === sId);
    const supplier = getSupplier(scenario.Supplier);
    
    for (const docRow of docsForScenario) {
        const docType = docRow.DocumentType;
        const rawData = {
            DocumentNumber: docRow.DocumentNumber,
            DocumentDate: docRow.DocumentDate,
            SupplierName: docRow.SupplierName,
            SupplierNIF: docRow.SupplierNIF,
            TotalAmount: docRow.TotalAmount
        };
        
        const docData = applyInconsistencies(sId, docType, rawData);
        
        // Define filename mapping safely handling special characters
        let fileNameSuffix = docType.replace(/ /g, '_');
        const pdfPath = path.join(sDir, `${sId}_${fileNameSuffix}.pdf`);
        
        const doc = new PDFDocument({ margin: 50 });
        doc.pipe(fs.createWriteStream(pdfPath));
        
        if (docType === 'Proforma') {
            createProforma(doc, scenario, docData, supplier);
        } else if (docType === 'P.O' || docType === 'P.O.') {
            createPO(doc, scenario, docData, supplier);
        } else if (docType === 'Comprovativo Agendamento' || docType === 'Payment Scheduling Proof') {
            createSchedulingProof(doc, scenario, docData, supplier);
        } else if (docType === 'Comprovativo Pagamento' || docType === 'Payment Proof') {
            createPaymentProof(doc, scenario, docData, supplier);
        } else if (docType === 'Recibo' || docType === 'Receipt') {
            createReceipt(doc, scenario, docData, supplier);
        } else {
            // Default
            doc.fontSize(20).text(docType, { align: 'center' }).moveDown();
            drawHeader(doc, '', supplier, docData);
            doc.text(`Valor: ${docData.TotalAmount}`);
        }
        
        doc.end();
        generatedCount++;
        
        if (sampleNames.length < 5) {
            sampleNames.push(`${sId}_${fileNameSuffix}.pdf`);
        }
    }
}

console.log(`Generated ${generatedCount} PDF files in ${outDir}.`);
console.log('Samples:');
sampleNames.forEach(s => console.log(' - ' + s));
