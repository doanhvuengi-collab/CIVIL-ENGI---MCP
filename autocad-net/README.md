# AutoCAD .NET bridge + MCP server (`autocad-net`)

Thay cho server COM cũ (`E:\OneDrive\.Claude\AutoCAD-MCP\server.py`). Ngày 24/11 mất hơn 6 giờ vì COM chậm,
lỗi "Call was rejected" và treo khi lệnh DCE chờ nhập. Bộ này chạy **bên trong AutoCAD 2023**:

```
Claude Code ──MCP(stdio)──> mcp-server (Python) ──named pipe \\.\pipe\CivilEngiAcad──> CivilEngi.AcadBridge.dll (trong acad.exe)
Script Python ──acad_bridge.Acad()────────────────┘
```

| Vấn đề với COM | Cách plugin xử lý |
|---|---|
| Gọi chậm từng thuộc tính qua COM, "Call was rejected" khi AutoCAD bận | Lệnh cấp cao chạy trong process AutoCAD, một lần gọi là một transaction, trả JSON. AutoCAD bận thì trả lỗi `busy` sau 15 s thay vì treo |
| Gọi lệnh .NET chờ nhập thì COM treo tới khi user bấm Esc | `command` gửi bằng `SendStringToExecute` rồi theo dõi. Lệnh đứng chờ nhập quá `stall_s` thì trả `waiting_input`, sau đó gọi `command_cancel` hoặc gửi tiếp input |
| Chọn bằng window chỉ bắt đối tượng trên màn hình | `query` đọc thẳng database theo bbox, layer, loại, tên block, chữ hoặc XData |
| `ms.Count` / chỉ số để tìm đối tượng vừa vẽ | `command` ghi lại mọi đối tượng mới tạo (ObjectAppended) và trả handle cùng bbox |
| Bấm "Yes" bằng PowerShell UIA, SendKeys chiếm bàn phím | `auto_answer` / `dialog_click` bấm nút hộp thoại bằng Win32 ngay trong AutoCAD |
| Sửa khi đang có lệnh chạy dễ hỏng dữ liệu | Mọi lệnh sửa bị chặn khi `CMDACTIVE≠0` (muốn bỏ qua thì gửi `force=true`) |

## Cài đặt (máy Windows có AutoCAD 2023)

Cần .NET SDK 6 trở lên (`winget install Microsoft.DotNet.SDK.8`) và Python 3.10 trở lên.

```powershell
cd <repo>
powershell -ExecutionPolicy Bypass -File autocad-net\install\install.ps1
```

Script sẽ build DLL, copy vào `%APPDATA%\Autodesk\ApplicationPlugins\CivilEngiAcadBridge.bundle` (AutoCAD tự nạp mỗi lần mở),
`pip install mcp`, rồi `claude mcp add --scope user autocad-net`. Server COM cũ (`autocad`) vẫn giữ nguyên, hai server chạy song song được.
Nếu AutoCAD đang mở thì NETLOAD DLL trong bundle để dùng ngay. Gõ `CEBRIDGE` trong AutoCAD để xem trạng thái, log nằm ở `%TEMP%\CivilEngiAcadBridge.log`.

Muốn build tay: `dotnet build autocad-net\plugin\CivilEngi.AcadBridge -c Release`. Lệnh này tải gói `AutoCAD.NET 24.2.0` từ nuget.org để biên dịch và không copy DLL của AutoCAD ra.

## Lệnh của plugin (tool MCP `acad_*`)

| Nhóm | cmd | Việc |
|---|---|---|
| Trạng thái | `ping`, `status`, `doc_activate` | bản vẽ đang mở, CMDACTIVE, DBMOD, layout hiện hành |
| Layout | `layouts`, `layout_set`, `layout_rename`, `layout_reorder` | đổi layout (`zoom_extents`), đổi tên, sắp tab |
| Đọc | `query`, `get` | quét theo bbox (cross/inside/center), layer, loại DXF, block, chữ, XData |
| Sửa | `delete`, `move`, `copy`, `set_props`, `set_text`, `set_attributes`, `set_xdata` | theo handle; `copy` deep-clone giữ attribute/XData |
| Vẽ | `draw` | nhiều line/polyline/text/mtext/block/dim/circle trong một lần gọi |
| Soát sheet | `viewports`, `viewport_set`, `viewport_cut_check`, `find_overlaps` | vùng model của từng view, chữ/block bị viewport cắt, khung Defpoints trùng |
| DCE | `dce_sck` | nhân/đặt số cấu kiện XData DcePro (thay `sua_sck.py`), mặc định chỉ xem trước |
| Lệnh | `command`, `command_status`, `command_cancel`, `dialogs`, `dialog_click` | chạy VDAll/VD/TAOMV…, tự bấm hộp thoại, không treo |
| Khác | `zoom`, `regen`, `var_get`, `var_set`, `backup`, `save`, `pick_points`, `select` | `backup` = copy file dwg đã lưu (cảnh báo nếu còn thay đổi chưa lưu) |

Giao thức: mỗi dòng là một JSON `{"id":1,"cmd":"query","args":{...}}`, plugin trả một dòng
`{"id":1,"ok":true,"ms":12,"result":...}` hoặc `{"ok":false,"kind":"busy|error|exception","error":"..."}`.
Pipe chỉ mở cho user Windows đang đăng nhập.

## Dùng trong script (thay `server._doc()` COM)

```python
import sys; sys.path.insert(0, r"<repo>\autocad-net\mcp-server")
from acad_bridge import Acad
acad = Acad()

# VDAll T3-7, tự bấm "giống nhau chỉ vẽ 1 lần?" -> Yes
r = acad.call("command", command="VDAll\n668000,50000\n", layout="Model",
              auto_answer=[{"text": "giống nhau", "button": "Yes"}], timeout_s=900)
print(r["state"], r["new_count"], r["bbox"])

# VD 1 dầm ở vùng tạm rồi dời về đúng chỗ (thay vd_thay2.py)
r = acad.call("command", command="VD\n2000000,0\n")
ten = acad.call("query", blocks=["Dce_KhTenDam"], bbox=[667000, -1e6, 833000, 1e6])
# ... tính dx, dy từ tiêu đề cũ, xoá đúng handle dầm cũ (sau khi đã kiểm), rồi:
acad.call("move", handles=r["handles"], dx=-1332000, dy=12345)

# Soát sheet dầm: tên dầm / chữ MC bị viewport cắt
cut = acad.call("viewport_cut_check", layouts=["03-401", "03-402"])
```

Dòng lệnh: `python -m acad_bridge status`, `python -m acad_bridge query "{\"layers\":[\"Defpoints\"],\"count_only\":true}"`.

## Giới hạn hiện tại
- Form Excel DCE (Export Drawing Data, Find dầm) vẫn chạy bằng script Python và UIA như cũ. Plugin chỉ lo phía AutoCAD.
- `backup` copy file đã lưu trên đĩa. Bản vẽ còn thay đổi chưa lưu thì plugin cảnh báo chứ không tự lưu.
- `viewport_cut_check` bỏ qua viewport xoay (twist) và chỉ xét khung chữ nhật của viewport clip.
- Lệnh chờ nhập: plugin báo `waiting_input` nhưng chưa đọc được nội dung prompt.
