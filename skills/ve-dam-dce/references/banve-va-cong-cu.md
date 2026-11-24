# BanVe, BEAM DCE và công cụ

## Cột BanVe (đã xác minh)
| Cột | Nội dung |
|---|---|
| 1 | MARK (vd 3BY4-1) |
| 3 | Inner Length |
| 17 | B ; 19/20 Bmax/Hmax |
| 23–26 | Mặt gối Point1/2 X,Y (toạ độ CAD local) |
| 35–38 | Trục ETABS X1,Y1,X2,Y2 (m) |
| 40 / 47 | Tên trục trái / phải |
| 45 | Trục trái → mặt gối phải |
| 46 / 53 | Gối trái / phải (mm) |
| 52 | Trục phải → mặt gối trái |
| 54 / 55 | Loại gối trái / phải |
| 56 / 57 | Giật mép trên (BD) / mép dưới (BE) |
| 58 / 59 | Dầm giao cột trái / phải (vd 250x400) |
| 60 / 61 | **Lệch dầm giao trái / phải** so tâm cột |
| 62 | Cao độ |
| 65–70 | Dầm giao giữa nhịp, cốt treo, vai bò |
| 71 (BS) | **Số cấu kiện** (trống = 1) |

Mọi số liệu riêng từng dầm sửa được ở BanVe. Không ở BanVe: ô chung của file (DCE_Pro_Beam) và thép tính (TinhDam).

## BEAM DCE (chỉ đọc để kiểm, không sửa)
D gối trái, E nhịp, F gối phải, AI/AK lệch trục trái/phải, AU/AV giật trên/dưới, cột 51/52 lệch dầm giao, BJ số cấu kiện. DCE tự suy lệch trục = trục − (mặt gối ± gối/2).

## XData DcePro
(0) tên cấu kiện, (1) số hiệu, (2) liên kết, (3) Ø, **(4) số cấu kiện**, (5) T|B. Với dim dầm, (4) là chiều dài – không sửa.

## Script (E:\OneDrive\.Claude\AutoCAD-MCP)
| Script | Việc |
|---|---|
| server.py | MCP AutoCAD (COM) |
| vdall_all.py / vdall_run.py <tag> "<x,y>" | VDAll các tầng, tự bấm Yes |
| vd_ve.py | VD lại dầm đã xoá, dời về đúng chỗ |
| vd_thay2.py | VD thay dầm tại chỗ (xoá cả Defpoints) |
| taomv2/3.ps1, taomv_run.ps1 x1 y1 x2 y2 | Bấm Tạo MV + chọn crossing + điểm chèn |
| fix_layouts.py, renumber.py, taborder.py, zoom_all.py | Định dạng, đánh số, sắp tab, zoom sheet |
| dam_geom.py | Trích cột/dầm mặt bằng ra JSON |
| kiem_goi3.py | So mặt gối / gối với cột CAD |
| kiem_daugiao.py <tầng> [-v] [-d] | So gối + lệch dầm giao với mặt bằng |
| sua_sck.py x0 x1 heso [TEN=n] [--run] | Nhân / đặt số cấu kiện XData |
| khung_long.py [--run] | Tìm / xoá khung view trùng |
| dem_dam.py | Liệt kê tên dầm theo cột tầng |
| clouds.py | Khoanh mây nghi vấn (layer "CLAUDE_NGHI VAN") |
| soat_sheet*.py, doi_zigzag.py | CÓ LỖI – không dùng lại |

Khi plugin .NET sẵn sàng, các script COM trên sẽ được thay bằng lệnh plugin tương ứng.
