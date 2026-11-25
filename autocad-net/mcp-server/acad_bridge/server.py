"""MCP server "autocad-net": Claude dieu khien AutoCAD qua plugin .NET CivilEngi.AcadBridge (named pipe).

Thay cho server COM cu (server.py, ten "autocad"): nhanh hon, khong "Call was rejected",
khong treo khi lenh cho nhap (tra trang thai waiting_input), quet theo vung khong phu thuoc man hinh.

Chay: python -m acad_bridge.server   (hoac python server.py trong thu muc mcp-server)
"""
from __future__ import annotations

from typing import Any

try:  # mcp >= 2
    from mcp.server.mcpserver import MCPServer as _Server
except ImportError:  # mcp 1.x
    from mcp.server.fastmcp import FastMCP as _Server

from .client import Acad, AcadError, instances

mcp = _Server("autocad-net")
_acad = Acad()


def _call(cmd: str, **args) -> Any:
    try:
        return _acad.call(cmd, **args)
    except AcadError as ex:
        return {"ok": False, "kind": ex.kind, "error": str(ex)}


# ----------------------------------------------------------------- phien AutoCAD
@mcp.tool()
def acad_instances() -> Any:
    """Cac phien AutoCAD dang chay plugin (pipe, pid, ban ve dang mo) va ban ve dich hien tai."""
    return {"target_doc": _acad.doc, "instances": instances()}


@mcp.tool()
def acad_target(doc: str | None) -> Any:
    """Chon ban ve dich cho MOI lenh sau (chuoi con ten file, vd 'DOANH TRAI - 1'). Bat buoc khi mo nhieu AutoCAD.
    Plugin tu tim dung phien AutoCAD dang mo ban ve do. doc=None de bo chon."""
    _acad.target(doc)
    if not doc:
        return {"target_doc": None}
    try:
        _acad._resolve()
        return {"target_doc": doc, "pipe": _acad._resolved}
    except AcadError as ex:
        return {"ok": False, "kind": ex.kind, "error": str(ex)}


# ----------------------------------------------------------------- trang thai
@mcp.tool()
def acad_status() -> Any:
    """Trang thai AutoCAD: ban ve dang mo, ban ve hien hanh, layout, lenh dang chay (CMDACTIVE), DBMOD."""
    return _call("status")


@mcp.tool()
def acad_ping() -> Any:
    """Kiem tra plugin con song va main thread AutoCAD co ranh khong (tra nhanh ca khi AutoCAD ban)."""
    return _call("ping")


@mcp.tool()
def acad_activate_doc(doc: str) -> Any:
    """Chuyen sang ban ve co ten chua chuoi doc (vd 'DOANH TRAI')."""
    return _call("doc_activate", doc=doc)


# ----------------------------------------------------------------- layout
@mcp.tool()
def acad_layouts(doc: str | None = None) -> Any:
    """Danh sach layout theo thu tu tab: ten, tab_order, current, so viewport."""
    return _call("layouts", doc=doc)


@mcp.tool()
def acad_set_layout(name: str, zoom_extents: bool = False, doc: str | None = None) -> Any:
    """Chuyen layout hien hanh ('Model' hoac ten sheet). zoom_extents=True de Z E luon."""
    return _call("layout_set", name=name, zoom_extents=zoom_extents, doc=doc)


@mcp.tool()
def acad_rename_layout(old: str, new: str, doc: str | None = None) -> Any:
    """Doi ten layout."""
    return _call("layout_rename", old=old, new=new, doc=doc)


@mcp.tool()
def acad_reorder_layouts(names: list[str], doc: str | None = None) -> Any:
    """Sap tab layout theo dung thu tu names (layout khong co trong danh sach giu phia sau)."""
    return _call("layout_reorder", names=names, doc=doc)


# ----------------------------------------------------------------- doc doi tuong
@mcp.tool()
def acad_query(
    bbox: list[float] | None = None,
    mode: str = "cross",
    layers: list[str] | None = None,
    types: list[str] | None = None,
    blocks: list[str] | None = None,
    text: str | None = None,
    space: str = "model",
    xdata_app: str | None = None,
    has_xdata: str | None = None,
    details: bool = False,
    count_only: bool = False,
    limit: int = 2000,
    doc: str | None = None,
) -> Any:
    """Quet doi tuong trong database (khong phu thuoc man hinh / zoom).

    bbox=[x0,y0,x1,y1]; mode: cross (cham vung) | inside (nam tron) | center (tam trong vung).
    layers/types/blocks/text ho tro wildcard * ?. types theo ten DXF: LINE, LWPOLYLINE, INSERT, TEXT, MTEXT,
    DIMENSION, HATCH, CIRCLE, ARC, VIEWPORT... blocks = ten block hieu dung (dynamic block lay ten goc).
    text: loc theo noi dung chu / thuoc tinh block (vd '*3BY4*'). space: model | current | ten layout.
    xdata_app: tra kem XData app do ('*' = tat ca). has_xdata: chi lay doi tuong co XData app do (vd 'DcePro').
    Tra: total, by_type, by_layer, items (handle, type, layer, bbox, text/attributes/points...).
    Vd lay ten dam DCE trong cot T3-7: bbox=[667000,-1e6,833000,1e6], blocks=['Dce_KhTenDam'].
    """
    return _call("query", bbox=bbox, mode=mode, layers=layers, types=types, blocks=blocks, text=text,
                 space=space, xdata_app=xdata_app, has_xdata=has_xdata, details=details,
                 count_only=count_only, limit=limit, doc=doc)


@mcp.tool()
def acad_get(handles: list[str], xdata_app: str | None = None, doc: str | None = None) -> Any:
    """Thong tin chi tiet doi tuong theo handle (kem XData neu xdata_app, '*' = tat ca)."""
    return _call("get", handles=handles, xdata_app=xdata_app, doc=doc)


# ----------------------------------------------------------------- sua doi tuong
@mcp.tool()
def acad_delete(handles: list[str], force: bool = False, doc: str | None = None) -> Any:
    """Xoa doi tuong theo handle. QUY TAC: chi xoa doi tuong Claude tao hoac user da dong y.
    Bi chan khi AutoCAD dang chay lenh (force=True de bo qua)."""
    return _call("delete", handles=handles, force=force, doc=doc)


@mcp.tool()
def acad_move(handles: list[str], dx: float, dy: float, doc: str | None = None) -> Any:
    """Dich doi tuong mot doan (dx, dy)."""
    return _call("move", handles=handles, dx=dx, dy=dy, doc=doc)


@mcp.tool()
def acad_copy(handles: list[str], dx: float, dy: float, doc: str | None = None) -> Any:
    """Copy doi tuong (giu thuoc tinh block, XData) roi dich (dx, dy). Tra map handle cu -> handle moi."""
    return _call("copy", handles=handles, dx=dx, dy=dy, doc=doc)


@mcp.tool()
def acad_set_props(handles: list[str], layer: str | None = None, color: int | None = None,
                   linetype: str | None = None, linetype_scale: float | None = None,
                   width: float | None = None, doc: str | None = None) -> Any:
    """Doi layer (tu tao neu chua co) / mau (256 = ByLayer) / linetype / linetype_scale / width (polyline)."""
    return _call("set_props", handles=handles, layer=layer, color=color, linetype=linetype,
                 linetype_scale=linetype_scale, width=width, doc=doc)


@mcp.tool()
def acad_set_text(handles: list[str], text: str, doc: str | None = None) -> Any:
    """Sua noi dung TEXT / MTEXT / text override cua DIMENSION."""
    return _call("set_text", handles=handles, text=text, doc=doc)


@mcp.tool()
def acad_set_attributes(handles: list[str], values: dict[str, Any], doc: str | None = None) -> Any:
    """Sua thuoc tinh block {TAG: gia tri} (va tham so dynamic block cung ten)."""
    return _call("set_attributes", handles=handles, values=values, doc=doc)


@mcp.tool()
def acad_set_xdata(handles: list[str], app: str, values: list[list[Any]], doc: str | None = None) -> Any:
    """Thay toan bo XData cua app: values=[[1000,'chuoi'],[1070,5],...] (khong gom dong 1001). [] = xoa XData app."""
    return _call("set_xdata", handles=handles, app=app, values=values, doc=doc)


@mcp.tool()
def acad_draw(items: list[dict[str, Any]], layer: str | None = None, color: int | None = None,
              space: str = "model", doc: str | None = None) -> Any:
    """Ve nhieu doi tuong trong 1 lan goi (1 transaction). Tra danh sach handle.

    Moi item: {kind, ...}:
      line: start[x,y], end[x,y] | polyline: points[[x,y],..], closed | circle: center, radius
      text: text, at, height=250, rotation_deg, align(left|center|middle) | mtext: text, at, height, width
      block: name, at, rotation_deg, scale, attributes{TAG:val}, dynamic{prop:val}
      dim_aligned: p1, p2, at (diem duong kich thuoc), text (de trong = so do)
    Item co the co layer, color rieng.
    """
    return _call("draw", items=items, layer=layer, color=color, space=space, doc=doc)


# ----------------------------------------------------------------- viewport / soat sheet
@mcp.tool()
def acad_viewports(layouts: list[str] | None = None, doc: str | None = None) -> Any:
    """Viewport cua cac layout (trong = tat ca): handle, khung giay, ti le (scale_1_to), model_bbox vung model nhin thay, locked."""
    return _call("viewports", layouts=layouts, doc=doc)


@mcp.tool()
def acad_set_viewport(handles: list[str], center: list[float] | None = None, width: float | None = None,
                      height: float | None = None, view_center: list[float] | None = None,
                      custom_scale: float | None = None, locked: bool | None = None, doc: str | None = None) -> Any:
    """Sua viewport: center/width/height (giay), view_center (toa do model), custom_scale (giay/model), locked."""
    return _call("viewport_set", handles=handles, center=center, width=width, height=height,
                 view_center=view_center, custom_scale=custom_scale, locked=locked, doc=doc)


@mcp.tool()
def acad_viewport_cut_check(layouts: list[str] | None = None, types: list[str] | None = None,
                            layers: list[str] | None = None, blocks: list[str] | None = None,
                            tol: float = 1.0, doc: str | None = None) -> Any:
    """Soat sheet: doi tuong model bi vien viewport cat (mot phan trong, mot phan ngoai).
    Mac dinh xet TEXT, MTEXT, INSERT -> bat loi ten 'DAM ...', chu mat cat, block bi cat. visible_ratio = ti le phan nhin thay."""
    return _call("viewport_cut_check", layouts=layouts, types=types, layers=layers, blocks=blocks, tol=tol, doc=doc)


@mcp.tool()
def acad_find_overlaps(layers: list[str] | None = None, bbox: list[float] | None = None,
                       min_ratio: float = 0.5, space: str = "model", doc: str | None = None) -> Any:
    """Tim polyline kin chong nhau (mac dinh layer Defpoints = khung view DCE trung sau khi VD lai).
    Tra cac cap chong va older_handles (khung cu hon) – CHI xoa khi user dong y."""
    return _call("find_overlaps", layers=layers, bbox=bbox, min_ratio=min_ratio, space=space, doc=doc)


@mcp.tool()
def acad_dce_sck(x0: float, x1: float, factor: int = 1, set: dict[str, int] | None = None,
                 run: bool = False, doc: str | None = None) -> Any:
    """So cau kien DCE (XData DcePro truong 4) cho doi tuong co tam x trong [x0, x1).
    factor: nhan he so (vd 5 cho T3-7). set: dat thang {TEN: so} (vd {'3BY4': 30}). run=False chi xem truoc."""
    return _call("dce_sck", x0=x0, x1=x1, factor=factor, set=set, run=run, doc=doc)


# ----------------------------------------------------------------- dan trang chi tiet dam (gom 1 lenh)
@mcp.tool()
def acad_dam_dan_trang(mau: str, window: list[float], tang: str, prefix: str, start: int | None = None,
                       ticks: dict[str, bool] | None = None, fix: bool = True, fix_names: bool = True,
                       net_khuat: bool = True, zoom: bool = True, shots: bool = True, max_shots: int = 6,
                       backup: bool = True, save: bool = False, title_find: str | None = None,
                       title_format: str | None = None, timeout_s: float = 900, max_layouts: int = 40,
                       allow_template_viewports: bool = False, doc: str | None = None) -> Any:
    r"""DAN TRANG 1 TANG DAM DCE trong 1 lan goi (thay hang tram lenh nho):
    backup -> TAOMV (form LayoutVPs: chon layout mau `mau`, tick o, Tao MV, chon C theo window, diem chen 20.8,413.2)
    -> doi ten to <prefix>-<start..> + khung ten 'DẦM TẦNG n - 0x' -> sap tab -> soat & sua tung to:
    net ngan DCE_NetKhuat (LTscale 0.5, width 0.3, gióng thang; to 1 dam thi xoa), doi ten dam bi view cat vao giua view
    (chi ten + TL, khong dong zigzag/net cat model), bao: dam vat 2 to, view noi ngan, view chong / ngoai khung, chu bi cat
    -> Z E tung to -> chup anh cac to co loi (PNG trong %TEMP%\CivilEngiShots). save=False: tu luu 1 lan cuoi dot.
    window=[x1,y1,x2,y2] vung model cua tang. tang: '1', '3-4', 'TUM', 'MAI'. start bo trong = so 4xx lon nhat + 1.
    mau PHAI la to trong chi co khung ten (mau co viewport -> bao loi, vd to thep san 03-306 tung tao 149 to).
    ticks: chi dat khi can, mac dinh giu nguyen o tick tren form. Tao > max_layouts to -> dung, khong doi ten.
    Tra gon: so_loi, loi[], da_sua[], anh[]. CHI XEM ANH cac to trong 'anh'."""
    return _call("dam_dan_trang", mau=mau, window=window, tang=tang, prefix=prefix, start=start, ticks=ticks,
                 fix=fix, fix_names=fix_names, net_khuat=net_khuat, zoom=zoom, shots=shots, max_shots=max_shots,
                 backup=backup, save=save, title_find=title_find, title_format=title_format, timeout_s=timeout_s,
                 max_layouts=max_layouts, allow_template_viewports=allow_template_viewports, doc=doc)


@mcp.tool()
def acad_dam_soat_to(layouts: list[str] | None = None, from_: str | None = None, to: str | None = None,
                     pattern: str | None = None, fix: bool = False, fix_names: bool = True, net_khuat: bool = True,
                     shots: bool = False, max_shots: int = 6, doc: str | None = None) -> Any:
    """Soat cac to dam co san (layouts[] hoac from_='02-426', to='02-436', hoac pattern regex).
    fix=False chi bao loi (mac dinh); fix=True sua net ngan + doi ten dam bi cat nhu acad_dam_dan_trang.
    Loi: ten_bi_cat, chu_bi_cat, dam_vat_2_to, view_noi_ngan, vp_chong, vp_ngoai_khung, net_ngan, khong_thay_ten."""
    return _call("dam_soat_to", layouts=layouts, **{"from": from_}, to=to, pattern=pattern, fix=fix,
                 fix_names=fix_names, net_khuat=net_khuat, shots=shots, max_shots=max_shots, doc=doc)


@mcp.tool()
def acad_dam_net_khuat(pattern: str = r"^\d+-4\d\d$", net_ltscale: float = 0.5, net_width: float = 0.3,
                       run: bool = True, doc: str | None = None) -> Any:
    """BUOC CUOI BAT BUOC moi dot dan trang: moi net ngan DCE_NetKhuat trong cac layout khop pattern
    (mac dinh xx-4xx) ve linetype scale 0.5 (va width 0.3). run=False chi dem."""
    return _call("dam_net_khuat", pattern=pattern, net_ltscale=net_ltscale, net_width=net_width, run=run, doc=doc)


@mcp.tool()
def acad_layout_shot(layouts: list[str], max_width: int = 1400, doc: str | None = None) -> Any:
    """Chup to (Z E roi chup cua so AutoCAD) ra PNG, tra duong dan. Chi chup to can xem de tiet kiem token."""
    return _call("layout_shot", layouts=layouts, max_width=max_width, doc=doc)


# ----------------------------------------------------------------- lenh AutoCAD / add-in
@mcp.tool()
def acad_command(command: str, auto_answer: list[dict[str, str]] | None = None, wait: bool = True,
                 timeout_s: float = 120, stall_s: float = 20, layout: str | None = None,
                 return_handles: bool = True, force: bool = False, doc: str | None = None) -> Any:
    r"""Chay lenh AutoCAD / DCE (VDAll, VD, TAOMV, ...) va ghi lai doi tuong moi tao.

    command: chuoi go vao dong lenh, xuong dong = Enter. Vd "VDAll\n668000,50000\n", "VD\n2000000,0\n".
    auto_answer: tu bam hop thoai, vd [{"text": "giống nhau", "button": "Yes"}] (title/text = chuoi con).
    layout: chuyen layout truoc khi chay (vd "Model").
    state tra ve: done | waiting_input (lenh dung cho nhap qua stall_s giay – KHONG treo nhu COM) |
    dialog_open | running (qua timeout_s, goi acad_command_status de cho tiep).
    Kem new_count, bbox, by_layer, handles cac doi tuong moi (dung de dich / xoa dung doi tuong minh tao).
    Bi chan neu AutoCAD dang chay lenh khac; force=True de gui tiep input vao lenh dang cho.
    """
    return _call("command", command=command, auto_answer=auto_answer, wait=wait, timeout_s=timeout_s,
                 stall_s=stall_s, layout=layout, return_handles=return_handles, force=force, doc=doc)


@mcp.tool()
def acad_command_status(timeout_s: float = 0, stall_s: float = 20, return_handles: bool = True) -> Any:
    """Theo doi tiep lenh vua gui bang acad_command (cho them toi da timeout_s giay)."""
    return _call("command_status", timeout_s=timeout_s, stall_s=stall_s, return_handles=return_handles)


@mcp.tool()
def acad_command_cancel(doc: str | None = None) -> Any:
    """Huy lenh dang chay (Esc Esc) – dung khi lenh dung cho nhap."""
    return _call("command_cancel", doc=doc)


@mcp.tool()
def acad_dialogs() -> Any:
    """Liet ke hop thoai / form dang mo cua AutoCAD (tieu de, noi dung, cac nut)."""
    return _call("dialogs")


@mcp.tool()
def acad_dialog_click(button: str, title: str | None = None, text: str | None = None) -> Any:
    """Bam nut tren hop thoai AutoCAD (khong dung SendKeys). title/text = chuoi con de chon dung hop thoai."""
    return _call("dialog_click", button=button, title=title, text=text)


@mcp.tool()
def acad_dialog_select(value: str, title: str | None = None, index: int | None = None, current: str | None = None) -> Any:
    """Chon muc trong combobox cua form add-in (vd o Layout cua TAOMV = '03-201').
    index = thu tu combo trong form (xem acad_dialogs), hoac current = chuoi con cua text dang hien."""
    return _call("dialog_select", value=value, title=title, index=index, current=current)


# ----------------------------------------------------------------- man hinh, bien, luu
@mcp.tool()
def acad_zoom(mode: str = "extents", bbox: list[float] | None = None, center: list[float] | None = None,
              height: float | None = None, doc: str | None = None) -> Any:
    """Zoom: extents (Z E) | window (bbox) | center (center + height)."""
    return _call("zoom", mode=mode, bbox=bbox, center=center, height=height, doc=doc)


@mcp.tool()
def acad_regen(doc: str | None = None) -> Any:
    """REGEN ban ve."""
    return _call("regen", doc=doc)


@mcp.tool()
def acad_get_vars(names: list[str]) -> Any:
    """Doc bien he thong (vd CMDACTIVE, DBMOD, CTAB, CLAYER)."""
    return _call("var_get", names=names)


@mcp.tool()
def acad_set_var(name: str, value: Any) -> Any:
    """Dat bien he thong."""
    return _call("var_set", name=name, value=value)


@mcp.tool()
def acad_backup(path: str | None = None, dir: str | None = None, doc: str | None = None) -> Any:
    """Copy file .dwg dang luu sang path / dir (mac dinh <thu muc dwg>\\_backup\\ten_yyMMdd_HHmmss.dwg). Lam truoc khi sua."""
    return _call("backup", path=path, dir=dir, doc=doc)


@mcp.tool()
def acad_save(doc: str | None = None) -> Any:
    """Luu ban ve (QSAVE). Quy tac: chi luu 1 lan khi xong ca dot viec."""
    return _call("save", doc=doc)


@mcp.tool()
def acad_pick_points(count: int = 1, prompt: str = "Chon diem", timeout_s: int = 300) -> Any:
    """Nho user click diem tren AutoCAD (Esc de dung som)."""
    return _call("pick_points", count=count, prompt=prompt, timeout_s=timeout_s)


@mcp.tool()
def acad_select(prompt: str = "Chon doi tuong", xdata_app: str | None = None, timeout_s: int = 300) -> Any:
    """Nho user chon doi tuong tren AutoCAD; tra thong tin tung doi tuong."""
    return _call("select", prompt=prompt, xdata_app=xdata_app, timeout_s=timeout_s)


@mcp.tool()
def acad_call(cmd: str, args: dict[str, Any] | None = None) -> Any:
    """Goi thang lenh bat ky cua plugin (xem danh sach bang cmd='help')."""
    return _call(cmd, **(args or {}))


def main():
    mcp.run()


if __name__ == "__main__":
    main()
