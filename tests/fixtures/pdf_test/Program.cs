using System;
using PdfiumViewer;
using System.IO;

namespace PdfTest 
{
    class Program 
    {
        static void Main(string[] args) 
        {
            try 
            {
                using var doc = PdfDocument.Load(@"c:\dev\alpla-portal\tests\fixtures\ocr_validation\1_invoices_clean\clean_invoice_01.pdf");
                Console.WriteLine($"Pages: {doc.PageCount}");
                var text = doc.GetPdfText(0);
                Console.WriteLine($"Text Extracted (Length: {text.Length}):\n{text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
