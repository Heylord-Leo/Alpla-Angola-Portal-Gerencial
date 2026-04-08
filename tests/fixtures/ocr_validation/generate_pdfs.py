from fpdf import FPDF
import os
import random
from datetime import datetime, timedelta

def create_invoice(filename, supplier, nif, date_str, currency, items, total, pages=1):
    pdf = FPDF()
    pdf.set_auto_page_break(auto=True, margin=15)
    
    for page in range(pages):
        pdf.add_page()
        pdf.set_font("Arial", 'B', 16)
        pdf.cell(0, 10, "INVOICE", ln=True, align='C')
        pdf.set_font("Arial", '', 12)
        pdf.cell(0, 10, f"Supplier: {supplier}", ln=True)
        pdf.cell(0, 10, f"NIF: {nif}", ln=True)
        pdf.cell(0, 10, f"Invoice Number: INV-{random.randint(1000, 9999)}", ln=True)
        pdf.cell(0, 10, f"Date: {date_str}", ln=True)
        pdf.cell(0, 10, f"Currency: {currency}", ln=True)
        
        pdf.ln(10)
        pdf.set_font("Arial", 'B', 12)
        pdf.cell(80, 10, "Description", 1)
        pdf.cell(40, 10, "Quantity", 1)
        pdf.cell(40, 10, "Unit Price", 1, ln=True)
        
        pdf.set_font("Arial", '', 12)
        for item in items:
            pdf.cell(80, 10, item['desc'], 1)
            pdf.cell(40, 10, str(item['qty']), 1)
            pdf.cell(40, 10, str(item['price']), 1, ln=True)
        
        if page == pages - 1:
            pdf.ln(10)
            pdf.set_font("Arial", 'B', 14)
            pdf.cell(0, 10, f"Total Amount: {total} {currency}", ln=True, align='R')
            
        else:
            pdf.ln(20)
            pdf.cell(0, 10, f"Continued on next page... (Page {page+1}/{pages})", ln=True, align='C')

    pdf.output(filename)


def create_contract(filename, title, pages=3):
    pdf = FPDF()
    pdf.set_auto_page_break(auto=True, margin=15)
    for page in range(pages):
        pdf.add_page()
        pdf.set_font("Arial", 'B', 16)
        pdf.cell(0, 10, title, ln=True, align='C')
        pdf.set_font("Arial", '', 12)
        pdf.ln(10)
        for _ in range(30):
            pdf.multi_cell(0, 10, "This is a placeholder textual block for a legally binding contract. " * 3)
            pdf.ln(5)
    pdf.output(filename)

base_dir = r"c:\dev\alpla-portal\tests\fixtures\ocr_validation"
os.makedirs(os.path.join(base_dir, "1_invoices_clean"), exist_ok=True)
os.makedirs(os.path.join(base_dir, "2_invoices_multipage"), exist_ok=True)
os.makedirs(os.path.join(base_dir, "3_contracts"), exist_ok=True)
os.makedirs(os.path.join(base_dir, "4_edge_cases"), exist_ok=True)

# 1. Clean Invoices
create_invoice(os.path.join(base_dir, "1_invoices_clean", "clean_invoice_01.pdf"), "Acme Corp", "123456789", "2026-04-01", "USD", [{"desc": "Server Rack", "qty": 1, "price": 1000.00}], "1000.00")
create_invoice(os.path.join(base_dir, "1_invoices_clean", "clean_invoice_02.pdf"), "Tech Supplies Angola", "987654321", "2026-04-02", "AOA", [{"desc": "Monitors", "qty": 10, "price": 50000.00}], "500000.00")
create_invoice(os.path.join(base_dir, "1_invoices_clean", "clean_invoice_03.pdf"), "Global Logistics", "111222333", "2026-04-03", "EUR", [{"desc": "Freight", "qty": 1, "price": 450.00}], "450.00")
create_invoice(os.path.join(base_dir, "1_invoices_clean", "clean_invoice_04.pdf"), "Consulting LLC", "444555666", "2026-04-04", "USD", [{"desc": "Advisory Hours", "qty": 5, "price": 200.00}], "1000.00")

# 2. Multi-page Invoices
create_invoice(os.path.join(base_dir, "2_invoices_multipage", "multipage_invoice_01.pdf"), "Big Supplier", "90909090", "2026-04-05", "AOA", [{"desc": "Bulk Materials", "qty": 100, "price": 50.0}], "5000.0", pages=4)
create_invoice(os.path.join(base_dir, "2_invoices_multipage", "multipage_invoice_02.pdf"), "Enterprise Software", "70707070", "2026-04-05", "EUR", [{"desc": "Licenses", "qty": 50, "price": 100.0}], "5000.0", pages=3)
create_invoice(os.path.join(base_dir, "2_invoices_multipage", "multipage_invoice_03.pdf"), "Office Supplies Co", "60606060", "2026-04-06", "USD", [{"desc": "Desks", "qty": 20, "price": 250.0}], "5000.0", pages=5)

# 3. Public Contracts
create_contract(os.path.join(base_dir, "3_contracts", "contract_public_01_nda.pdf"), "Non-Disclosure Agreement", pages=3)
create_contract(os.path.join(base_dir, "3_contracts", "contract_public_02_msa.pdf"), "Master Service Agreement", pages=5)

# 4. Edge Cases 
# For edge case, we'll combine low DPI by rendering an image or adding a poor font. 
# We'll just create a text-heavy poorly formatted invoice. 
pdf = FPDF()
pdf.add_page()
pdf.set_font("Courier", 'I', 8)
pdf.multi_cell(0, 4, "INVOICE   \n Supplier: Bad scan LLC \n NIF: 9999 \n Total: 50 USD \n \n" * 20)
pdf.output(os.path.join(base_dir, "4_edge_cases", "edge_case_01.pdf"))

pdf = FPDF()
pdf.add_page()
pdf.set_font("Helvetica", '', 24)
pdf.cell(0, 10, "GIANT TEXT INVOICE 99 EUR Total", ln=True)
pdf.output(os.path.join(base_dir, "4_edge_cases", "edge_case_02.pdf"))

print("PDFs Generated!")
