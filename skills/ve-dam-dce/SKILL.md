---
name: ve-dam-dce
description: VẼ DẦM DCE — kiểm/sửa số liệu dầm trong Excel DCE (BanVe, DCE_Pro_Beam, BEAM DCE), vẽ dầm trong AutoCAD bằng VDAll/VD, dàn trang TAOMV và soát sheet theo checklist bắt buộc. Dùng khi user bảo vẽ dầm, sửa dầm, kiểm gối/lệch trục/dầm giao, VD/VDAll, TAOMV, dàn trang sheet dầm, số cấu kiện dầm, hoặc nhắc "VẼ DẦM DCE".
---

# VẼ DẦM DCE

Công cụ trên máy user: `E:\OneDrive\.Claude\AutoCAD-MCP` (server.py MCP COM + script Python). Bảng cột BanVe / BEAM DCE và danh sách script: `references/banve-va-cong-cu.md`. Tài liệu tổng hợp: `docs/DCE-TONG-HOP.md` trong repo.

> **Ghi chú chuyển đổi:** phần thao tác CAD (VDAll, VD, TAOMV, soát sheet, quét đối tượng) hiện chạy qua COM. Khi plugin .NET trong AutoCAD (named pipe, lệnh cấp cao trả JSON) sẵn sàng, chuyển các bước đó sang lệnh plugin; quy tắc số liệu và checklist giữ nguyên.

## Cách làm việc (bắt buộc)
- Tự kiểm, tự sửa sai phạm số liệu; chỉ hỏi khi bản vẽ thật sự mập mờ (hatch không rõ, không có số liệu âm sàn, không có nét giật cấp). Không đổ lỗi DCE.
- **Mọi số liệu ETABS coi là sai cho tới khi đo lại trên mặt bằng CAD**: gối, lệch trục, lệch dầm giao, chiều dài. Tự đo, không hỏi.
- Báo ngắn gọn, giọng nhẹ, luôn ước lượng thời gian còn lại. Chờ CAD/Excel tối đa 3–5 s, chạy liên tục.
- Quy trình: kiểm số liệu TẤT CẢ tầng → vẽ toàn bộ → kiểm hàng loạt → dàn trang 1 lần. Không vẽ–dàn–kiểm–vẽ lại.
- Save 1 lần khi xong đợt (SaveCopyAs backup trước khi sửa). Không mở Excel ngầm. Chỉ xoá đối tượng do mình tạo. Báo trước khi dùng SendKeys (chiếm bàn phím ~30 s).
- **Export Drawing Data do user bấm** (người phụ trách tự xác nhận dữ liệu cuối). File chưa Export → không vẽ, báo user.
- User bảo dừng → dừng ngay, chỉ ghi nhớ.

## Luồng dữ liệu
`ETABS → NoiLuc → TinhDam →[Chuyển DL tính => vẽ]→ DCE_Pro_Beam ⇄[Next/Back]⇄ BanVe →[Export Drawing Data]→ BEAM DCE → VDAll/VD`
- **BanVe** là nguồn: sửa thẳng BanVe rồi user Export (nhanh nhất), hoặc sửa form DCE_Pro_Beam + Next/Back.
- KHÔNG sửa BEAM DCE (bị ghi đè khi Export). KHÔNG bấm "Lưu dữ liệu". Next/Back ở dầm chưa sửa cũng ghi đè giá trị form cũ.
- Ô chung DCE_Pro_Beam (dò 1 lần mỗi workbook, áp cho mọi dầm): ĐK thép C đỡ lớp TC = 6, Đk đai C nối 2 thép giá = 6 (trừ khi chỉ định khác), neo theo dự án (thường gặp: trên 41d, dưới 35d).
- Form "Xuất dữ liệu bản vẽ": UIA chọn tab **DceAddin** trước, Invoke nút "Export Drawing Data" ngoài cùng phải (nhóm BEAM). Nạp dầm: WM_SETTEXT tên dầm vào ô EDIT trống trên-trái → BM_CLICK "Find" → kiểm DCE_Pro_Beam!F2.

## Quy tắc số liệu
- **Gối ≠ 0**: điền bề rộng dầm giao vuông góc, tối thiểu 200; buộc = 0 thì hình thái gối = "không có cột trên/dưới".
- **Gối = bề rộng cột dọc dầm** đo trên mặt bằng (cột tầng trên có thể nhỏ hơn tầng dưới, vd biên 300 trên / 400 dưới).
- **Lệch trục so tâm cột**: đo từng gối, kể cả khi trục nằm trong gối. |lệch| ≤ ½ gối, trục không nằm ngoài cột.
- **Lệch dầm giao (BanVe 60/61)**: dầm biên sát mép ngoài cột → (bề rộng cột − b)/2; dấu theo chiều dầm (đầu trái ra ngoài = âm, đầu phải ra ngoài = dương). Cột 400 → 75, 300 → 25, 250 với dầm 250 → 0, 500 → 125, 750 → 250. Dầm giao không được lòi ra ngoài cột. Chạy `kiem_daugiao.py <tầng> [-v]`.
- **Giật cao độ**: xoá ±3600 (DCE lấy nội lực bao nhiều tầng); h chạy suốt → giật dưới 0; chỉ giữ khi h đổi giữa 2 nhịp kề.
- **WC / sàn hạ**: hatch phủ dầm → dầm âm mặt trên theo sàn (vd −50). Mũi tên + "50" trên mép dầm = ranh bậc sàn (dầm không âm). Nét ĐỨT = dầm hạ theo sàn; nét LIỀN = dầm nhô, chỉ sàn hạ (T.LINK).
- **Dầm giao**: kèm cốt treo (vd 8d8a50) + vai bò (vd 2T16), nhiều dầm ngăn bằng "/".
- **Số cấu kiện** = số tầng (T3-7 → 5); dầm gộp DCE tự nhân khi VDAll (6 dầm × 5 tầng = 30).

## Vẽ (AutoCAD)
- ActiveLayout = Model trước VDAll. Thứ tự T1 → T2 → T3-7 → Tum → Mái; tầng chưa sẵn thì chờ. Cột vẽ tầng cách nhau ≤ 50.000 (từ mép phải tầng trước; trừ khi vướng chữ dầm dài).
- VDAll: `SendCommand("VDAll\nx,y\n")`, hộp "giống nhau chỉ vẽ 1 lần?" → Yes (`vdall_all.py`).
- Thay 1 dầm: xoá dầm cũ cả khung Defpoints → `vd_ve.py <tên>`: VD ở (2.000.000, 0), dời x = điểm VDAll gốc của cột tầng, y theo vị trí dầm cũ. VD lẻ 1 dầm của nhóm gộp → đặt lại tên gộp + `sua_sck.py x0 x1 1 TEN=n --run`.
- Sau khi vẽ: kiểm khung trùng (`khung_long.py`), không còn đối tượng tạm x > 1.9M, đủ tên dầm mỗi tầng (`dem_dam.py`).

## TAOMV + đánh số
- Form LayoutVPs → Tạo MV → chọn bằng **C** (crossing; gõ W bị Unikey đổi thành Ư) + 2 góc + Enter → điểm chèn 20.8,413.2.
- Đánh số layout: xx-101..199 cọc/móng; 201..299 cột; 301..399 MB dầm sàn; **401..499 chi tiết dầm** liên tục theo tầng; 501..599 thép sàn; 601..699 thang, ramp, bể nước, mái đón. Khung tên "CHI TIẾT / DẦM TẦNG n - 0x" (`fix_layouts.py`, `renumber.py`, `taborder.py`).

## Thống kê thép
- DBim Apps → THỐNG KÊ THÉP → Quét TK / Tổng hợp TK. Bước số lượng hàng: để sẵn 45 → Enter.
- Chưa biết đủ các bước thì không tự gọi lệnh qua COM (COM treo khi lệnh .NET chờ nhập, tới khi user Esc).

## Bẫy COM
- "Call was rejected" khi AutoCAD bận → retry. Cần ~1.5 s sau khi đổi layout.
- Selection window chỉ bắt đối tượng trên màn hình → dùng Select all + lọc bbox.
- `soat_sheet*.py`, `doi_zigzag.py` có lỗi – không dùng lại.

---

## CHECKLIST BẮT BUỘC (tick từng mục, không bỏ bước)

### A. Số liệu (mỗi workbook, trước khi vẽ)
- [ ] Ô chung DCE_Pro_Beam: ĐK thép C đỡ lớp TC = 6; Đk đai C nối 2 thép giá = 6; neo theo dự án.
- [ ] Giật cao độ: xoá ±3600; h suốt → giật dưới 0; chỉ giữ khi h đổi giữa 2 nhịp kề.
- [ ] Giật mặt trên âm WC theo hatch / nét liền–đứt.
- [ ] Dầm giao + cốt treo + vai bò đủ.
- [ ] Lệch trục ≤ ½ gối và khớp mặt bằng. Scan hàng loạt mọi file tầng trước khi vẽ.
- [ ] Lệch dầm giao (60/61) đo trên mặt bằng: `kiem_daugiao.py <tầng>` cho mọi tầng có mặt bằng.
- [ ] Không gối nào = 0.
- [ ] Sửa ở BanVe hoặc form + Next/Back (không sửa BEAM DCE, không "Lưu dữ liệu").
- [ ] Đọc lại BanVe + BEAM DCE xác nhận đã nhận số mới.
- [ ] User bấm Export Drawing Data.

### B. Vẽ
- [ ] Thứ tự tầng T1 → T2 → T3-7 → Tum → Mái.
- [ ] ActiveLayout = Model trước VDAll.
- [ ] Khoảng cách giữa các cột vẽ tầng ≤ 50.000.

### C. TAOMV
- [ ] Layout mẫu chuẩn của dự án (vd 03-201), không phải layout nháp.
- [ ] Tick: New-Copy, Tự động xếp khung tên, Sắp xếp tối ưu, **Cắt đúng trục** (mặc định bỏ trống!).
- [ ] Khung 511.5 × 406.8.
- [ ] Chụp form (PrintWindow) xác nhận trước khi bấm Tạo MV.

### D. Soát từng sheet sau TAOMV
- [ ] Dầm và hàng mặt cắt của nó cùng sheet.
- [ ] Không có mẩu view lẻ; chỉ kéo dãn view dầm bị cắt cụt (tới mép phải 531.8), không kéo hết.
- [ ] View không thừa khoảng trống lớn.
- [ ] Tên "DẦM …" + TL không bị viewport cắt (dầm dài 2 hàng → dời tên).
- [ ] Chữ tên MC không chồng / tràn.
- [ ] Khung tên CHI TIẾT / DẦM TẦNG n - 0x; số sheet liên tục.
- [ ] Sheet bất thường (view to/trống, MC tách khỏi dầm) → kiểm số liệu dầm trước (vd giật ±3600).
- [ ] Tờ chỉ 1 dầm → xoá nét DCE_NetKhuat.
- [ ] Kéo dãn view → chỉ dời zigzag (layer DCE_MCZiCZac) bị view đè trong layout; KHÔNG move zigzag model hàng loạt.
- [ ] Hàng MC tràn → B1 dời dầm lên; B2 dời MC lên; B3 thu view MC; B4 copy view MC xuống hàng dưới. Chật quá → để mặc định + báo.
- [ ] Mỗi layout dàn xong: Z ↵ E ↵ (Zoom Extents) trong paper space.
- [ ] Chụp từng sheet tự kiểm trước khi báo user.
