r"""Client named pipe cho plugin CivilEngi.AcadBridge (khong can pywin32, khong COM).

Dung trong script:
    from acad_bridge import Acad
    acad = Acad()                      # pipe \\.\pipe\CivilEngiAcad
    acad.status()
    r = acad.call("query", bbox=[668000, 0, 800000, 60000], blocks=["Dce_KhTenDam"])
    acad.call("command", command="VDAll\n668000,50000\n",
              auto_answer=[{"text": "giống nhau", "button": "Yes"}], timeout_s=600)

Dong lenh:
    python -m acad_bridge status
    python -m acad_bridge query "{\"layers\": [\"Defpoints\"], \"count_only\": true}"
"""
from __future__ import annotations

import itertools
import json
import os
import sys
import time

PIPE_NAME = os.environ.get("CIVIL_ACAD_PIPE", "CivilEngiAcad")


class AcadError(RuntimeError):
    """Plugin tra loi loi (tham so sai, doi tuong khong ton tai...)."""

    def __init__(self, message: str, kind: str = "error"):
        super().__init__(message)
        self.kind = kind


class AcadBusy(AcadError):
    """AutoCAD dang ban (dang chay lenh / tinh toan). Thu lai sau hoac command_cancel."""


class Acad:
    def __init__(self, pipe: str = PIPE_NAME, connect_timeout: float = 10.0):
        self.path = r"\\.\pipe\%s" % pipe
        self.connect_timeout = connect_timeout
        self._ids = itertools.count(1)

    def _open(self):
        t0 = time.time()
        while True:
            try:
                return open(self.path, "r+b", buffering=0)
            except FileNotFoundError:
                raise AcadError(
                    "Khong thay plugin AcadBridge (pipe %s). AutoCAD da mo chua? Plugin da NETLOAD / cai bundle chua? "
                    "Go CEBRIDGE trong AutoCAD de kiem tra." % self.path, "not_running")
            except OSError as ex:  # 231 = tat ca instance dang ban
                if time.time() - t0 > self.connect_timeout:
                    raise AcadBusy("Pipe dang ban: %s" % ex, "busy")
                time.sleep(0.1)

    def call(self, cmd: str, **args):
        req = {"id": next(self._ids), "cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}
        data = (json.dumps(req, ensure_ascii=False) + "\n").encode("utf-8")
        f = self._open()
        try:
            f.write(data)
            buf = bytearray()
            while not buf.endswith(b"\n"):
                chunk = f.read(1 << 16)
                if not chunk:
                    break
                buf += chunk
        finally:
            f.close()
        if not buf:
            raise AcadError("Plugin dong ket noi ma khong tra loi", "exception")
        resp = json.loads(buf.decode("utf-8"))
        if not resp.get("ok"):
            kind = resp.get("kind", "error")
            cls = AcadBusy if kind == "busy" else AcadError
            raise cls(resp.get("error", "loi khong ro"), kind)
        return resp.get("result")

    # tien ich hay dung
    def status(self):
        return self.call("status")

    def ping(self):
        return self.call("ping")


def call(cmd: str, **args):
    return Acad().call(cmd, **args)


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    if not argv:
        print(__doc__)
        return 1
    cmd = argv[0]
    args = json.loads(argv[1]) if len(argv) > 1 else {}
    try:
        res = Acad().call(cmd, **args)
    except AcadError as ex:
        print(json.dumps({"ok": False, "kind": ex.kind, "error": str(ex)}, ensure_ascii=False, indent=1))
        return 2
    print(json.dumps(res, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
