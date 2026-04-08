import json
import urllib.request
import urllib.parse
from urllib.error import URLError, HTTPError
import os

def post_multipart(url, file_path):
    import io
    import uuid
    boundary = uuid.uuid4().hex
    
    with open(file_path, 'rb') as f:
        file_bytes = f.read()
        
    filename = os.path.basename(file_path)
    mime_type = "application/pdf"
    
    body = io.BytesIO()
    body.write(f'--{boundary}\r\n'.encode('utf-8'))
    body.write(f'Content-Disposition: form-data; name="file"; filename="{filename}"\r\n'.encode('utf-8'))
    body.write(f'Content-Type: {mime_type}\r\n\r\n'.encode('utf-8'))
    body.write(file_bytes)
    body.write(b'\r\n')
    body.write(f'--{boundary}--\r\n'.encode('utf-8'))
    
    body_val = body.getvalue()
    
    req = urllib.request.Request(url, data=body_val)
    req.add_header('Content-Type', f'multipart/form-data; boundary={boundary}')
    req.add_header('Content-Length', str(len(body_val)))
    
    try:
         with urllib.request.urlopen(req) as response:
             resp_body = response.read()
             return json.loads(resp_body)
    except HTTPError as e:
         return {"error": f"HTTPError: {e.code} / {e.read().decode('utf-8')}"}
    except URLError as e:
         return {"error": f"URLError: {e.reason}"}
    except Exception as e:
         return {"error": f"Exception: {str(e)}"}

url = "http://localhost:5000/api/v1/requests/direct-ocr"

files = [
    r"c:\dev\alpla-portal\tests\fixtures\ocr_validation\1_invoices_clean\clean_invoice_01.pdf",
    r"c:\dev\alpla-portal\tests\fixtures\ocr_validation\1_invoices_clean\clean_invoice_02.pdf",
    r"c:\dev\alpla-portal\tests\fixtures\ocr_validation\2_invoices_multipage\multipage_invoice_01.pdf",
    r"c:\dev\alpla-portal\tests\fixtures\ocr_validation\3_contracts\contract_public_01_nda.pdf",
    r"c:\dev\alpla-portal\tests\fixtures\ocr_validation\4_edge_cases\edge_case_01.pdf"
]

results = []
for p in files:
    print(f"Testing {os.path.basename(p)}...")
    res = post_multipart(url, p)
    print(json.dumps(res, indent=2))
    results.append({"file": os.path.basename(p), "result": res})

with open("ocr_test_revalidation.json", "w") as f:
    json.dump(results, f, indent=2)

print("Done.")
