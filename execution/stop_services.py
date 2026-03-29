import os
import subprocess
import signal
import time

def stop_process_by_port(port):
    print(f"Checking for process on port {port}...")
    try:
        # Get process ID on port
        if os.name == 'nt': # Windows
            cmd = f'netstat -ano | findstr LISTENING | findstr :{port}'
            output = subprocess.check_output(cmd, shell=True).decode()
            if output:
                for line in output.strip().split('\n'):
                    if f':{port}' in line:
                        pid = line.strip().split()[-1]
                        print(f"Killing process {pid} on port {port}...")
                        os.system(f"taskkill /F /PID {pid}")
                        time.sleep(1)
        else: # Linux/Mac
            cmd = f'lsof -t -i:{port}'
            pid = subprocess.check_output(cmd, shell=True).decode().strip()
            if pid:
                print(f"Killing process {pid} on port {port}...")
                os.kill(int(pid), signal.SIGKILL)
                time.sleep(1)
    except subprocess.CalledProcessError:
        print(f"No process found on port {port}.")

def main():
    print("Stopping Alpla Portal services...")
    stop_process_by_port(5000)
    stop_process_by_port(5173)
    print("All portal services stopped.")

if __name__ == "__main__":
    main()
