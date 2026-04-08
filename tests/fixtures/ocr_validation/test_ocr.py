import sys
import glob
import json
import urllib.request
import urllib.parse
from urllib.error import URLError, HTTPError
import mimetypes

def post_multipart(url, file_path):
    import io
    import uuid
    boundary = uuid.uuid4().hex
    
    with open(file_path, 'rb') as f:
        file_bytes = f.read()
        
    filename = file_path.split('\\')[-1]
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
         print(f"HTTPError: {e.code} / {e.read().decode('utf-8')}")
         return None
    except URLError as e:
         print(f"URLError: {e.reason}")
         return None

url = "http://localhost:5000/api/v1/requests/direct-ocr"

files = glob.glob(r"c:\dev\alpla-portal\tests\fixtures\ocr_validation\**\*.pdf", recursive=True)

results = []
for p in files:
    print(f"Testing {p}...")
    res = post_multipart(url, p)
    if res:
         print(json.dumps(res, indent=2))
         results.append({"file": p, "result": res})
    else:
         print("Failed")
         results.append({"file": p, "result": None})

with open("ocr_test_results.json", "w") as f:
    json.dump(results, f, indent=2)
