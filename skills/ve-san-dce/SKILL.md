---
name: ve-san-dce
description: VẼ SÀN DCE (THÉP SÀN DCE) — rải thép sàn (lớp trên thép mũ, lớp dưới chạy suốt) trong AutoCAD bằng block/layer DCE của user. Dùng khi user bảo vẽ/rải/sửa thép sàn, đặt ký hiệu thép sàn, làm tầng/khung mới, sửa thuộc tính tầng, số cấu kiện thép sàn, hoặc nhắc "VẼ SÀN DCE" / "THÉP SÀN DCE".
---

# VẼ SÀN DCE

Skill độc lập, không phụ thuộc skill vẽ dầm. Tài liệu tổng hợp (tham khảo): `docs/DCE-TONG-HOP.md`.

## Cách làm việc (bắt buộc)
- Tự kiểm, tự bố trí, ít hỏi; chỉ hỏi khi bản vẽ thật sự mập mờ.
- Báo ngắn gọn, giọng nhẹ, luôn ước lượng thời gian còn lại. Chờ CAD tối đa 3–5 s, chạy liên tục.
- Rải toàn bộ → kiểm hàng loạt → báo 1 lần. Save 1 lần khi xong đợt (SaveCopyAs backup trước khi sửa).
- Chỉ xoá đối tượng do mình tạo. Báo trước khi dùng SendKeys.
- User bảo dừng → dừng ngay, chỉ ghi nhớ.

Kỹ sư kết cấu nhiều năm kinh nghiệm: tự bố trí thép, chỉ dùng block/layer của user. Không dùng LISP. Công cụ: `E:\OneDrive\.Claude\AutoCAD-MCP`.

> **Ghi chú chuyển đổi:** phần thao tác CAD (copy bộ, vẽ thanh, nhận diện ô sàn, dời ký hiệu) hiện chạy qua Python COM. Khi plugin .NET trong AutoCAD sẵn sàng, chuyển sang lệnh plugin; quy tắc bố trí giữ nguyên.

## Kết nối (COM)
- Python COM, chạy với `dangerouslyDisableSandbox`. Chọn bản vẽ bằng biến môi trường `ACAD_DOC` (một phần tên file).
- "Call was rejected" = AutoCAD bận → chạy lại. AutoCAD treo/không thấy bản vẽ → user đóng hộp thoại add-in hoặc End task rồi mở lại.
- Không truyền chữ tiếng Việt qua tham số PowerShell 5.1 (lỗi mã hoá) → đặt trong file Python/JSON UTF-8.
- Xoá chỉ theo danh sách handle của chính mình (handles_*.txt), không xoá theo bộ lọc rộng.

## Bộ thép ("bộ" — luôn copy nguyên bộ)
- Block `Dce_KhThepSan` (attr SH, DKVAKC=%%c10a200) layer DCE_Block; chấm tròn + đường phân bố (DCE_Symbol, dimstyle Dce_209MV); dim 1000 (S-Dim, ISO-100).
- Thanh LỚP TRÊN: layer `S-Rebar Top`, móc xuống. LỚP DƯỚI: layer `S-Extra Rebar-Bot`, móc lên.
- Nguồn bộ mẫu: bản vẽ VT-KC-02 (nhà làm việc) — trên tag 1FCD53/bar 1FCD50/circle 1FCD52/dist 1FCD51/dim 1FCD93; dưới tag 20397C/bar 203979/circle 20397B/dist 20397A. Bản vẽ khác: CopyObjects từ bản vẽ này sang.
- XData `DcePro` (add-in Sửa thép): (0) tên cấu kiện, (1) số hiệu, (2) handle liên kết/KC200, (3) Ø, (4) SỐ CẤU KIỆN, (5) T|B;handle tag. Mỗi bộ liên kết handle riêng (`dcepro.py`).

## Quy trình mỗi khung
1. Đọc TIÊU ĐỀ dưới khung trong model (vd "MẶT BẰNG DẦM SÀN TẦNG 3-4"). Tên = "<phần sau 'DẦM SÀN '> - LỚP TRÊN/LỚP DƯỚI". Số cấu kiện = số tầng (3-4 → 2, 5, 6 → 2, 3-7 → 5). Không đoán theo thứ tự hàng/cột.
2. Không rải ở khung "MẶT BẰNG DẦM SÀN" có text dầm (mặt bằng dầm) hay "MẶT BẰNG CỘT".
3. Copy bộ mẫu vào khung → SỬA THUỘC TÍNH TẦNG TRÊN BỘ MẪU TRƯỚC → rồi mới copy ra (không rải xong mới sửa) → xoá bộ mẫu tạm.
4. Ô sàn tự nhận diện (`geom.py`: raster + tô vùng; lỗ = vùng có dấu X S-Grid). TẦNG 1: chỉ rải ô viền nét đứt (DASHED).
5. Chạy `run_frame.py <DOC> fx fy fw fh TOP|BOT <tag> [--panels ...] [--dashed-only] --draw`, tên tầng trong `names_<DOC>.json`; sửa tên/số cấu kiện: `set_floor_props.py`. Dời ký hiệu: `adjust_tags.py`.
6. Bố cục khung (hàng/cột nào là tầng nào, lớp trên/dưới) xác định lại cho từng bản vẽ theo tiêu đề, ghi vào bộ nhớ dự án, không dùng lại toạ độ của bản vẽ khác.

## Quy tắc bố trí
- LỚP TRÊN: rải cả X và Y. Phương X ra 1000 mỗi bên từ mép dầm; phương Y (thép đứng) ra L/3 nhịp tim-tim, làm tròn lên 50. Khe giữa 2 đầu < 1600 → chạy suốt. Ô đứng riêng (2 gối biên) → chạy suốt, không cắt.
- Dải phân bố hẹp < 1500 → gộp vào bộ bên cạnh (phủ ≥ 70%).
- LỚP DƯỚI: chạy suốt cả nhóm ô liền nhau, chỉ né lỗ; neo dầm 35d (thẳng trong dầm + móc lên, tối thiểu 110).
- Móc lớp trên: đầu trong sàn 110; đầu neo dầm thẳng 225 + bẻ 185 = 41d.
- **Thẳng hàng**: các bộ thép mũ cùng phương trong một dải ô liên tiếp (cùng hàng ô) phải nằm trên MỘT đường thẳng: phương X cùng toạ độ Y, phương Y cùng toạ độ X. Không để thanh bậc thang lệch cao độ giữa các nhịp. Vướng chữ thì dời ký hiệu, không dời thanh lệch khỏi hàng (user 24/11).
- Thép cạnh cách biên sàn ngoài cùng 25.
- Dim 1000 cách thanh 700 (vướng chữ → 500).
- **Dim thẳng hàng**: 2 dim 1000 hai bên dầm của cùng một bộ thép mũ phải chung một đường dim (cùng khoảng cách tới thanh), không so le. Vướng chữ thì dời cả cặp dim (cùng 500) hoặc dời ký hiệu, không dời riêng một dim (user 24/11).
- **Đường phân bố** nằm trong ô sàn, cách mép dầm một khoảng rõ ràng; không nằm sát hay đè lên nét dầm (user 24/11).
- **Ký hiệu và dim ở hai phía thanh**: ký hiệu + chữ "Ø..a.." một bên thanh, dim (1000 / L/3) bên kia; không kẹp chữ giữa dim và thanh, không để chữ sát thanh (user 24/11).
  - Phương X: vòng ký hiệu + chữ "Ø..a.." phía TRÊN thanh, cặp dim 1000 phía DƯỚI thanh.
  - Phương Y: chữ một bên thanh, dim L/3 bên kia.
- **Không nét nào cắt qua chữ**: đường phân bố, thanh, nét dim không được đi xuyên qua chữ dim ("1000", "2200") hay vòng/chữ ký hiệu; vướng thì dời ký hiệu hoặc dời đường phân bố dọc thanh (user 24/11).
- Ký hiệu: trượt dọc thanh tới chỗ thoáng, phải nằm trên thanh. Cả vòng tròn lẫn chữ "Ø..a.." nằm TRONG ô sàn: không đè mép dầm / nét viền ô / hatch cột; phương X đặt sát phía trong mép trái ô; phương Y chữ đứng gọn trong ô, không chồng ký hiệu phương kia chỗ giao.

## Checklist mỗi khung
- [ ] Tên + số cấu kiện đọc từ tiêu đề khung, đã sửa trên bộ mẫu trước khi copy.
- [ ] Đúng khung thép sàn (không phải MB dầm / MB cột); tầng 1 chỉ ô nét đứt.
- [ ] Lớp trên đủ X và Y; khe < 1600 đã nối suốt; dải < 1500 đã gộp.
- [ ] Thép mũ cùng phương trong cùng hàng ô thẳng hàng, không lệch bậc.
- [ ] Hai dim 1000 của mỗi bộ chung một đường, không so le.
- [ ] Đường phân bố cách mép dầm, không đè dầm; ký hiệu và dim nằm hai phía thanh.
- [ ] Phương X: ký hiệu trên thanh, dim dưới thanh; không nét nào cắt qua chữ.
- [ ] Lớp dưới chạy suốt nhóm ô, né lỗ, neo 35d.
- [ ] Ký hiệu + chữ nằm trong ô, không chồng nét/chữ (tự kiểm trước khi báo).
- [ ] Đã xoá bộ mẫu tạm; handle lưu trong handles_*.txt.

## Thống kê
Ribbon DBim Apps → THỐNG KÊ THÉP: "Quét TK" (quét), "Tổng hợp TK" (tổng hợp); bước số lượng hàng để 45 → Enter.
