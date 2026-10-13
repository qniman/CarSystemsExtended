"""
Send Python code to a running Blender with the Blender Lab "MCP" add-on bridge
(https://www.blender.org/lab/mcp-server/), the same way its MCP server does:
TCP localhost:9876, null-terminated JSON {"type": "execute", "code": ..., "strict_json": ...}.

Usage:
    python bridge.py script.py [more.py ...]   # files are concatenated, in order
    python bridge.py -c "result = {'v': bpy.app.version_string}"

The script may set `result = {...}` (JSON-serialisable) to return data.
"""

import json
import socket
import sys

HOST, PORT = "localhost", 9876


def execute(code, timeout=300.0):
    with socket.create_connection((HOST, PORT), timeout=timeout) as s:
        s.sendall((json.dumps({"type": "execute", "code": code, "strict_json": False}) + "\0").encode("utf-8"))
        buf = bytearray()
        while b"\0" not in buf:
            chunk = s.recv(65536)
            if not chunk:
                break
            buf.extend(chunk)
    return json.loads(bytes(buf).split(b"\0", 1)[0].decode("utf-8"))


def main(argv):
    if len(argv) >= 2 and argv[0] == "-c":
        code = argv[1]
    else:
        parts = []
        for path in argv:
            with open(path, encoding="utf-8") as f:
                parts.append(f.read())
        code = "\n\n".join(parts)
    resp = execute(code)
    if resp.get("stdout"):
        print(resp["stdout"], end="")
    if resp.get("stderr"):
        print(resp["stderr"], end="", file=sys.stderr)
    if resp.get("status") != "ok":
        print(resp.get("message", resp), file=sys.stderr)
        return 1
    print(json.dumps(resp.get("result", {}), indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
