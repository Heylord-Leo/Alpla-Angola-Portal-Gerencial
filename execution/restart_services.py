# ═══════════════════════════════════════════════════════════════════════════════
# execution/restart_services.py — Compatibility wrapper
#
# DEPRECATED: This script delegates entirely to execution/restart_services.ps1.
# It does NOT independently build, stop, or start services.
# It does NOT perform database validation.
#
# The canonical startup script is: execution/restart_services.ps1
# ═══════════════════════════════════════════════════════════════════════════════
import subprocess
import sys
import os

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    ps1_path = os.path.join(script_dir, "restart_services.ps1")

    if not os.path.isfile(ps1_path):
        print(f"[FATAL] Canonical script not found: {ps1_path}", file=sys.stderr)
        sys.exit(1)

    print("[INFO] Delegating to canonical startup script: execution/restart_services.ps1")
    result = subprocess.run(
        ["powershell", "-ExecutionPolicy", "Bypass", "-File", ps1_path],
        cwd=script_dir
    )
    sys.exit(result.returncode)

if __name__ == "__main__":
    main()
