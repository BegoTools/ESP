import subprocess
from pathlib import Path
from access_probe import ADB,SERIAL
result=subprocess.run([ADB,'-s',SERIAL,'exec-out','screencap','-p','-d','4619827551948147201'],capture_output=True,check=True,timeout=15)
(Path(__file__).parent/'training-screen.png').write_bytes(result.stdout)
