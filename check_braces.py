import sys
with open('scripts/server/setup-production-environment.ps1', 'r', encoding='utf-8') as f:
    lines = f.readlines()

depth = 0
for i, line in enumerate(lines, 1):
    for char in line:
        if char == '{':
            depth += 1
        elif char == '}':
            depth -= 1
    if depth < 0:
        print(f"Error: unmatched }} at line {i}")
        break

if depth > 0:
    print(f"Error: missing {depth} closing }}'s")
else:
    print("Braces are perfectly balanced!")
