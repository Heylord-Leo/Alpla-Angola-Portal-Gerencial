# OCR Validation Dataset (Phase 1)

This directory serves as the local, safe test suite for the Alpla Angola Portal Gerencial OCR extraction pipeline. It is intentionally small and consists of public/synthetic files designed to evaluate OpenAI Vision parsing without compromising real company data.

## Directory Structure
- `1_invoices_clean/`: Simple, 1-2 page typical commercial invoices (validates happy-path mapping).
- `2_invoices_multipage/`: Long invoices (3+ pages) useful for testing the 3-page stop limit in Phase 1.
- `3_contracts/`: Long textual agreements for upcoming Phase 2 (Text/Chunking approaches vs Raster limits).
- `4_edge_cases/`: Low DPI/scanned/handwritten PDF snippets to test stability limits.

## How to use
Copy your test PDFs into the respective folders following the naming convention:
`[source]_[type]_[id].pdf` (e.g., `w3c_invoice_01.pdf`).

Use the `worksheet.csv` provided in this directory to manually record extraction successes, token consumption, and errors.
