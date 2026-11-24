"""Diem chay MCP server khi dang ky bang duong dan file: python <...>\\mcp-server\\server.py"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from acad_bridge.server import main  # noqa: E402

if __name__ == "__main__":
    main()
