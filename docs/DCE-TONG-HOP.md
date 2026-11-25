# DCE – Tổng hợp quy trình và quy tắc

Kiến thức đã chốt khi vẽ dầm, thép sàn, dàn trang bằng add-in DCE Pro trong AutoCAD 2023 + Excel. Chi tiết thao tác và checklist nằm trong skill:

| Skill | Phạm vi |
|---|---|
| [`skills/ve-dam-dce`](../skills/ve-dam-dce/SKILL.md) | Số liệu dầm Excel DCE → VDAll/VD → TAOMV → soát sheet (kèm checklist A–D) |
| [`skills/ve-san-dce`](../skills/ve-san-dce/SKILL.md) | Rải thép sàn lớp trên / lớp dưới bằng block DCE |
| [`skills/ve-dam-dce/references/banve-va-cong-cu.md`](../skills/ve-dam-dce/references/banve-va-cong-cu.md) | Bảng cột BanVe, BEAM DCE, XData, danh sách script |

## 1. Nguyên tắc làm việc
- Claude là trợ lý chiến lược: tự kiểm, tự sửa sai phạm số liệu, chủ động đề xuất cách nhanh hơn, ít hỏi. Chỉ hỏi khi bản vẽ thật sự mập mờ.
- Mọi số liệu ETABS coi là chưa đúng cho tới khi đo lại trên mặt bằng CAD.
- Báo ngắn gọn, giọng nhẹ, luôn ước lượng thời gian còn lại.
- Vẽ toàn bộ → kiểm hàng loạt → dàn trang 1 lần. Save 1 lần cuối đợt, backup trước khi sửa.
- Export Drawing Data do người phụ trách tự bấm. User bảo dừng là dừng.
- Chỉ xoá đối tượng do mình tạo; không mở Excel ngầm; báo trước khi SendKeys.

## 2. Luồng dữ liệu DCE
```
ETABS → NoiLuc → TinhDam →[Chuyển DL tính => vẽ]→ DCE_Pro_Beam ⇄[Next/Back]⇄ BanVe →[Export Drawing Data]→ BEAM DCE → VDAll / VD (CAD)
```
BanVe là nguồn; không sửa BEAM DCE; không bấm "Lưu dữ liệu".

## 3. Đánh số layout
xx-101..199 cọc/móng · 201..299 cột · 301..399 MB dầm sàn · 401..499 chi tiết dầm · 501..599 thép sàn · 601..699 thang, ramp, bể nước, mái đón.

## 4. Thông số theo dự án
Mỗi dự án ghi riêng (bộ nhớ dự án, không đưa vào skill): đường dẫn Excel/DWG, neo thép, layout mẫu, toạ độ cột vẽ tầng, bố cục khung thép sàn, các nghi vấn còn tồn.

## 5. Kế hoạch
- **Plugin .NET (C#) trong AutoCAD 2023** thay COM: named pipe, lệnh cấp cao trả JSON (thay dầm, quét vùng, soát sheet…). Lý do: COM chậm, hay bị từ chối khi AutoCAD bận. Khi plugin sẵn sàng, phần thao tác CAD trong hai skill chuyển sang lệnh plugin.
- Tiếp tục đóng gói các quy trình khác (cột, móng, thang…) thành skill cùng khuôn.
