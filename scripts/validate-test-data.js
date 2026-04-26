const fs = require('fs');
const path = require('path');
const { parse } = require('csv-parse/sync');
const pdf = require('pdf-parse');

const dataDir = 'C:\\dev\\alpla-portal\\docs\\test-data';
const pdfsDir = path.join(dataDir, 'pdfs');
const matrixPath = path.join(dataDir, '04_DocumentMatrix.csv');

async function runValidation() {
    console.log("--- Starting Validation ---\n");

    // 1. Read CSV
    const csvData = fs.readFileSync(matrixPath, 'utf8');
    const records = parse(csvData, { columns: true, skip_empty_lines: true, delimiter: ';' });

    // Count tracking
    let totalScenarioFolders = 0;
    let totalPdfFiles = 0;
    let foldersWithNot5Pdfs = [];
    let datesClampedCount = 0;
    let invalidChronologyScenarios = [];
    
    const startDate = new Date('2026-03-26').getTime();
    const endDate = new Date('2026-04-26').getTime();

    // Group by scenario
    const scenarios = {};
    for (const r of records) {
        if (!scenarios[r.ScenarioId]) scenarios[r.ScenarioId] = [];
        scenarios[r.ScenarioId].push(r);
    }

    // 1. Count validation
    if (fs.existsSync(pdfsDir)) {
        const scenarioFolders = fs.readdirSync(pdfsDir).filter(f => fs.statSync(path.join(pdfsDir, f)).isDirectory());
        totalScenarioFolders = scenarioFolders.length;

        for (const sf of scenarioFolders) {
            const files = fs.readdirSync(path.join(pdfsDir, sf)).filter(f => f.endsWith('.pdf'));
            totalPdfFiles += files.length;
            if (files.length !== 5) {
                foldersWithNot5Pdfs.push(sf);
            }
        }
    }

    // 2. Date and Chronology validation
    for (const [scnId, docs] of Object.entries(scenarios)) {
        let prevTime = 0;
        let scnValid = true;

        const docOrder = ['Proforma', 'P.O', 'Comprovativo de Agendamento', 'Comprovativo de Pagamento', 'Recibo'];
        docs.sort((a, b) => docOrder.indexOf(a.DocumentType) - docOrder.indexOf(b.DocumentType));

        for (const doc of docs) {
            const dTime = new Date(doc.DocumentDate).getTime();
            
            // Check bounds
            if (dTime < startDate || dTime > endDate) {
                console.log(`Out of bounds date: ${doc.DocumentDate} in ${scnId}`);
                scnValid = false;
            }

            if (dTime === endDate) {
                datesClampedCount++;
            }

            // Check chronology
            if (dTime < prevTime) {
                console.log(`Invalid chronology in ${scnId}: ${doc.DocumentDate} comes after previous doc.`);
                scnValid = false;
            }
            prevTime = dTime;
        }

        if (!scnValid) {
            invalidChronologyScenarios.push(scnId);
        }
    }

    // 4. Sample visual validation
    const sampleScenarios = Object.keys(scenarios).sort(() => 0.5 - Math.random()).slice(0, 10);
    console.log(`\n--- Visual Validation for 10 Samples ---`);
    for (const scnId of sampleScenarios) {
        const docs = scenarios[scnId];
        let allMatch = true;
        
        for (const doc of docs) {
            // Find physical pdf
            const folderPath = path.join(pdfsDir, scnId);
            if (fs.existsSync(folderPath)) {
                const files = fs.readdirSync(folderPath);
                // Need to find the exact filename match or roughly match the document type
                // E.g. SCN-001_Proforma.pdf
                const typeStr = doc.DocumentType.replace(/ /g, '_');
                const file = files.find(f => f.includes(typeStr) || f.includes(typeStr.split('_')[0]));
                
                if (file) {
                    const pdfBuffer = fs.readFileSync(path.join(folderPath, file));
                    try {
                        const data = await pdf(pdfBuffer);
                        
                        // Depending on the format, we check if the date is in the text
                        // Format generated: 2026-04-10 or 10/04/2026? 
                        // PDF generator uses: new Date(DocumentDate).toLocaleDateString('pt-AO')
                        const dObj = new Date(doc.DocumentDate);
                        const ptDate = dObj.toLocaleDateString('pt-AO');
                        
                        if (!data.text.includes(ptDate) && !data.text.includes(doc.DocumentDate)) {
                            console.log(`Date mismatch in PDF: ${scnId} / ${doc.DocumentType}. Expected: ${ptDate} or ${doc.DocumentDate}`);
                            allMatch = false;
                        }
                    } catch (e) {
                        console.log(`Error reading PDF ${file}: ${e.message}`);
                        allMatch = false;
                    }
                } else {
                    console.log(`Could not find PDF for ${scnId} / ${doc.DocumentType}`);
                    allMatch = false;
                }
            }
        }
        console.log(`Checked ${scnId}: ${allMatch ? 'SUCCESS (Dates Match)' : 'FAILED'}`);
    }

    console.log(`\n--- Final Output ---`);
    console.log(`Total scenario folders found: ${totalScenarioFolders}`);
    console.log(`Total PDF files found: ${totalPdfFiles}`);
    console.log(`Folders without exactly 5 PDFs: ${foldersWithNot5Pdfs.length > 0 ? foldersWithNot5Pdfs.join(', ') : 'None'}`);
    console.log(`Number of documents dated 26/04/2026 (Clamped/Ceiling): ${datesClampedCount}`);
    console.log(`Scenarios with invalid chronology: ${invalidChronologyScenarios.length > 0 ? invalidChronologyScenarios.join(', ') : 'None'}`);
    console.log(`10 visually checked scenario IDs: ${sampleScenarios.join(', ')}`);
}

runValidation().catch(console.error);
