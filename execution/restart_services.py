import subprocess
import os
import signal
import time
import sys

def kill_process_by_port(port):
    try:
        # Find PID using port
        result = subprocess.check_output(f"netstat -ano | findstr :{port}", shell=True).decode()
        for line in result.splitlines():
            if "LISTENING" in line:
                pid = line.strip().split()[-1]
                print(f"Killing process {pid} on port {port}...")
                subprocess.run(f"taskkill /F /PID {pid}", shell=True, check=False)
                # Give it a moment to release the port
                time.sleep(1)
    except subprocess.CalledProcessError:
        print(f"No process found on port {port}.")

def restart_services():
    # 1. Kill existing processes
    print("Stopping existing services...")
    kill_process_by_port(5000) # Backend
    kill_process_by_port(5173) # Frontend

    # 2. Start Backend
    backend_dir = r"c:\dev\alpla-portal\src\backend\AlplaPortal.Api"
    print(f"Starting backend in {backend_dir}...")
    # Use start to run in a new window so it doesn't block
    subprocess.Popen("start dotnet run", cwd=backend_dir, shell=True)

    # 3. Start Frontend
    frontend_dir = r"c:\dev\alpla-portal\src\frontend"
    print(f"Starting frontend in {frontend_dir}...")
    subprocess.Popen("start npm run dev", cwd=frontend_dir, shell=True)

    print("Services restart initiated in separate windows.")
    print("Please check the new terminal windows for output.")

if __name__ == "__main__":
    restart_services()
