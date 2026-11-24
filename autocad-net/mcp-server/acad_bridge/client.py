r"""Client named pipe cho plugin CivilEngi.AcadBridge (khong can pywin32, khong COM).

Moi phien AutoCAD mo 1 pipe rieng \\.\pipe\CivilEngiAcad-<pid>. Client chon phien:
  - Acad(doc="DOANH TRAI - 1")  -> phien dang mo ban ve co ten chua chuoi do (an toan nhat khi mo 2 CAD)
  - bien moi truong CIVIL_ACAD_DOC = chuoi ten ban ve (mac dinh cho moi lan goi)
  - chi co 1 phien AutoCAD -> dung luon phien do
  - nhieu phien ma khong chi ro -> bao loi, liet ke cac phien (khong bao gio doan)

Dung trong script:
    from acad_bridge import Acad
    acad = Acad(doc="DOANH TRAI - 1")
    acad.status()
    acad.call("query", bbox=[668000, 0, 800000, 60000], blocks=["Dce_KhTenDam"])

Dong lenh:
    python -m acad_bridge instances
    python -m acad_bridge status
    set CIVIL_ACAD_DOC=DOANH TRAI - 1 & python -m acad_bridge layouts
"""
from __future__ import annotations

import itertools
import json
import os
import sys
import time

BASE = os.environ.get("CIVIL_ACAD_PIPE", "CivilEngiAcad")
PIPE_DIR = "\\\\.\\pipe\\"


class AcadError(RuntimeError):
    """Plugin tra loi loi (tham so sai, doi tuong khong ton tai...)."""

    def __init__(self, message: str, kind: str = "error"):
        super().__init__(message)
        self.kind = kind


class AcadBusy(AcadError):
    """AutoCAD dang ban (dang chay lenh / tinh toan). Thu lai sau hoac command_cancel."""


def _pipes() -> list[str]:
    try:
        names = os.listdir(PIPE_DIR)
    except OSError:
        return []
    return sorted(n for n in names if n == BASE or n.startswith(BASE + "-"))


def _raw_call(pipe: str, cmd: str, args: dict, rid: int, connect_timeout: float = 10.0):
    path = PIPE_DIR + pipe
    data = (json.dumps({"id": rid, "cmd": cmd, "args": args}, ensure_ascii=False) + "\n").encode("utf-8")
    t0 = time.time()
    while True:
        try:
            f = open(path, "r+b", buffering=0)
            break
        except FileNotFoundError:
            raise AcadError("Pipe %s khong con (AutoCAD da dong?)" % pipe, "not_running")
        except OSError as ex:  # 231 = tat ca instance dang ban
            if time.time() - t0 > connect_timeout:
                raise AcadBusy("Pipe dang ban: %s" % ex, "busy")
            time.sleep(0.1)
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
        raise (AcadBusy if kind == "busy" else AcadError)(resp.get("error", "loi khong ro"), kind)
    return resp.get("result")


def instances() -> list[dict]:
    """Cac phien AutoCAD dang chay plugin: pipe, pid, ban ve dang mo."""
    out = []
    for p in _pipes():
        item = {"pipe": p}
        try:
            st = _raw_call(p, "status", {}, 0, connect_timeout=3)
            item.update(pid=st.get("pid"), active_doc=st.get("active_doc"),
                        docs=[d.get("name") for d in st.get("docs", [])], bridge=st.get("bridge"))
        except AcadError as ex:
            item["error"] = str(ex)
        out.append(item)
    return out


class Acad:
    def __init__(self, doc: str | None = None, pipe: str | None = None, connect_timeout: float = 10.0):
        self.doc = doc if doc is not None else (os.environ.get("CIVIL_ACAD_DOC") or None)
        self.pipe = pipe
        self.connect_timeout = connect_timeout
        self._ids = itertools.count(1)
        self._resolved = None

    def target(self, doc: str | None):
        """Doi phien / ban ve dich (chuoi con cua ten file)."""
        self.doc = doc or None
        self._resolved = None

    def _resolve(self) -> str:
        if self.pipe:
            return self.pipe
        if self._resolved and self._resolved in _pipes():
            return self._resolved
        found = instances()
        live = [i for i in found if "error" not in i]
        if not found:
            raise AcadError("Khong thay plugin AcadBridge. AutoCAD da mo chua? Go CEBRIDGE trong AutoCAD de kiem tra.",
                            "not_running")
        if self.doc:
            want = self.doc.lower()
            hit = [i for i in live if any(want in (d or "").lower() for d in i.get("docs", []))]
            # uu tien phien co ban ve khop chinh xac ten (vd "DOANH TRAI" khong khop nham "DOANH TRAI - 1")
            exact = [i for i in hit if any((d or "").lower().rsplit(".", 1)[0].endswith(want) for d in i["docs"])]
            hit = exact or hit
            if len(hit) == 1:
                self._resolved = hit[0]["pipe"]
                return self._resolved
            raise AcadError("%s phien AutoCAD co ban ve chua '%s': %s" % (
                "Nhieu" if hit else "Khong co", self.doc, json.dumps(found, ensure_ascii=False)), "target")
        if len(live) == 1:
            self._resolved = live[0]["pipe"]
            return self._resolved
        raise AcadError("Dang mo %d phien AutoCAD, phai chi ro ban ve (doc=... / CIVIL_ACAD_DOC): %s" % (
            len(found), json.dumps(found, ensure_ascii=False)), "target")

    def call(self, cmd: str, **args):
        args = {k: v for k, v in args.items() if v is not None}
        # tham so doc cua lenh: neu chua co thi dung ban ve dich -> luon thao tac dung ban ve trong phien
        if self.doc and "doc" not in args and cmd not in ("ping", "status", "help", "dialogs", "dialog_click",
                                                            "command_status", "pick_points", "select", "var_get",
                                                            "var_set"):
            args["doc"] = self.doc
        return _raw_call(self._resolve(), cmd, args, next(self._ids), self.connect_timeout)

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
    try:
        if cmd == "instances":
            res = instances()
        else:
            args = json.loads(argv[1]) if len(argv) > 1 else {}
            res = Acad().call(cmd, **args)
    except AcadError as ex:
        print(json.dumps({"ok": False, "kind": ex.kind, "error": str(ex)}, ensure_ascii=False, indent=1))
        return 2
    print(json.dumps(res, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
