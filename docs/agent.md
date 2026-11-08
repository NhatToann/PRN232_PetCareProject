# agent.md — Quy trình làm việc của hội đồng agent (migration OJT → PRN232)

> Repo: `PRN232_PetCareProject` · Bổ sung cho `AGENTS.md` (mục §6) · **Ngôn ngữ: Tiếng Việt**
> Ngày: 08/11/2026

---

## 0. Nguyên tắc bất di bất dịch

| # | Nguyên tắc |
|---|---|
| 1 | **Chỉ đọc** trong `Projectojt/` (thư mục OJT) và `_slides_text/`. **Chỉ viết/sửa** trong `PRN232_PetCareProject/`. |
| 2 | Không chạy lại/sửa schema DB. Không đổi cấu hình `GoogleAuth` / `PayOS` đã có — chỉ đọc và tái sử dụng. |
| 3 | Không còn Node.js/npm trong kết quả cuối. |
| 4 | Không TODO / placeholder / mock / dữ liệu giả. Không migrate được nguyên vẹn ⇒ **dừng lại hỏi user**. |
| 5 | Không tự ý đổi UI/UX, màu sắc, bố cục, text, thứ tự, validation message của bản gốc. |
| 6 | Tuân thủ kiến trúc slide (Ch00–Ch10) kể cả khi khác thói quen. |
| 7 | Mỗi lát cắt: build **0 error** → test pass → tick `PARITY.md` → commit riêng. |
| 8 | Báo cáo luôn bằng tiếng Việt: **đã làm gì / còn gì / rủi ro gì**. Không nói "xong" khi chưa tick đủ checklist. |

---

## 1. Tối ưu RAM (bắt buộc trong suốt quá trình)

### 1.1. Nguyên tắc

1. **RAM là tài nguyên hữu hạn** — mỗi lần đọc file/agent sinh thêm là context + tiến trình. Phải tiết kiệm như tiết kiệm chi phí cloud.
2. **Không bao giờ đọc file lớn toàn bộ vào context.** Áp dụng nghiêm ngặt quy tắc ngân sách bảng dưới.
3. **Ưu tiên công cụ có lọc** (`Grep` với `output_mode`, `Glob`, `head_limit`) hơn `Read` toàn file.
4. **File > 300 dòng** ⇒ không `Read` trực tiếp; dùng `Grep` để lấy đúng vùng cần sửa, rồi mới `Read` với `offset`/`limit`.
5. **File > 1500 dòng hoặc > 60 KB** ⇒ **không đọc**; chỉ dùng `Grep` theo pattern để trích đoạn.
6. **Mỗi subagent chỉ đọc đúng phạm vi được giao** — không đọc toàn repo. Trả lời bằng **tóm tắt + đường dẫn file + số dòng**, không dán lại code dài.
7. **Subagent trả về markdown, không trả về file dump.** Ưu tiên ghi ra file đĩa (`docs/_inventory/*.md`) rồi đọc lại theo `offset/limit`.
8. **Không chạy song song nhiều agent đọc file nặng.** Tối đa **3 subagent đọc đồng thời**; các agent nặng (quét 144 asset, đọc 12.551 dòng CSS) chạy **tuần tự**.
9. **Dọn context sau mỗi lát cắt**: xoá biến tạm, không giữ 2 bản sao cùng nội dung trong bộ nhớ.
10. **Test/build dùng `dotnet build --nologo -v q`** để không đổ ngữ cảnh log.
11. **Không `cat`/`head` file qua terminal** để "xem nhanh" — dùng `Grep`/`Read` có giới hạn dòng.
12. **Ảnh**: không `Read` ảnh lớn; chỉ đọc khi thật sự cần kiểm tra giao diện, tối đa 1 ảnh/lần.

### 1.2. Ngân sách đọc theo loại file

| Loại | Giới hạn | Cách đọc |
|---|---|---|
| Slide text (`_slides_text/*.txt`) | 1 file / 1 lần | `Read` có `offset`/`limit` |
| `.razor`, `.cs` < 300 dòng | 1 lần | `Read` trọn |
| `.razor`, `.cs` 300–1500 dòng | 2 lần | `Grep` → `Read` vùng cần |
| `.cshtml`, CSS, JSON lớn | 3 lần | `Grep` (pattern + `head_limit`) |
| `node_modules`, `bin`, `obj`, `.git` | 0 lần | **cấm** đọc |
| Ảnh nhị phân | 0–1 lần | chỉ khi verify UI |
| SQL dump (866 KB × 2) | 0 lần | dùng kết quả kiểm kê đã lưu |

### 1.3. Giới hạn ngữ cảnh theo vai trò

| Vai trò | Phạm vi đọc tối đa | Kỳ vọng |
|---|---|---|
| Agent kiểm kê | 1 nhóm file | Bảng + đường dẫn, không dán code |
| Red Team | Tài liệu thiết kế + trích dẫn dòng | Phản biện theo mục số |
| Kiến trúc sư trưởng | Slide + inventory | Quyết định + sơ đồ |
| Implementer | 1 lát cắt (≤ 15 file) | Diff + build log |
| Verifier | 1 lát cắt | Tick + bằng chứng |

### 1.4. Bộ nhớ ngoài (persistent store)

- Ghi kết quả kiểm kê lớn vào `docs/_inventory/*.md` **thay vì** giữ trong context.
- Ghi trạng thái phiên làm việc vào `docs/PROGRESS.md` để có thể tiếp tục khi mất ngữ cảnh.
- Tin nhắn báo cáo cho user **luôn ngắn gọn** (≤ 40 dòng), không dán lại nội dung file.

---

## 2. Hội đồng khắc nghiệt (multi-agent council)

### 2.1. Cơ cấu

```
                    ┌──────────────────────┐
                    │  KIẾN TRÚC SƯ TRƯỞNG  │  (Lead Architect)
                    │  chốt kiến trúc, lát cắt│
                    └──────────┬───────────┘
                               │
          ┌────────────────────┼────────────────────┐
          │                    │                    │
   ┌──────▼──────┐     ┌───────▼────────┐   ┌───────▼───────┐
   │  CỐ VẤN      │     │  THẨM ĐỊNH      │   │  RED TEAM ×3  │
   │  (Advisor)   │     │  (Reviewer)     │   │  phá ý tưởng  │
   └──────────────┘     └────────────────┘   └───────┬───────┘
                                                      │ phản biện
                                                      ▼
                    ┌──────────────────────┐
                    │  CHIẾN LƯỢC TỐI ƯU │  ← vòng lặp ≥ 2 vòng
                    └──────────────────────┘
```

### 2.2. Vai trò và trách nhiệm

| Vai trò | Số agent | Nhiệm vụ | Đầu ra |
|---|---|---|---|
| **Kiến trúc sư trưởng** | 1 | Quyết định kiến trúc solution/project/layer, chia lát cắt, giải quyết mâu thuẫn slide | `ARCHITECT_DESIGN.md` |
| **Cố vấn** | 1 | Đề xuất phương án kỹ thuật tối ưu, cân bằng giữa slide và thực tế | Khuyến nghị + phương án A/B/C |
| **Thẩm định** | 1 | Đối chiếu từng cặp file/method/message/template/asset, đếm gap | `GAP_AUDIT.md` (số gap) |
| **Red Team** | 2–3 | Tìm lỗi kiến trúc, rủi ro runtime, đường ảnh hỏng, vi phạm ràng buộc | `RED_TEAM_REVIEW.md` (TOP N rủi ro) |
| **Agent kiểm kê** | 4 | Quét song song 4 mảng: API, Frontend, đích, luồng phức tạp | 4 file `_inventory/*.md` |
| **Implementer** | 1/lát cắt | Viết code theo lát cắt, build+test, tick checklist | Commit riêng |
| **Verifier** | 1/lát cắt | Chạy app thật, so sánh bản gốc | Tick + bằng chứng |

### 2.3. Quy trình vòng khắc nghiệt (GĐ2, mỗi lát cắt)

```
0. Kiến trúc sư trưởng đề xuất cách làm cho lát cắt  →  ghi vào PROGRESS.md
1. Red Team #1 phá (runtime, ràng buộc, RAM)          →  danh sách phản biện
2. Red Team #2 + Cố vấn phản biện chéo                 →  chốt rủi ro còn lại
3. Kiến trúc sư trưởng vòng 2: điều chỉnh chiến lược →  bản tốt nhất
4. Implementer triển khai (không tự thêm scope)
5. Thẩm định: build 0 error + test pass + tick PARITY
6. Verifier: chạy app, đối chiếu bản gốc (UI/status/data)
7. Commit riêng: feat(prn232): <slice> (LS-xx)
```

> **Quy tắc:** không có vòng nào được đóng khi Red Team còn rủi ro **mức 🔴 chưa được ghi nhận và giải quyết hoặc ghi nhận chấp nhận rủi ro rõ ràng**.

---

## 3. Quy trình báo cáo (bắt buộc)

Mỗi lần báo cáo cho user phải có đủ 4 phần:

```
1. ĐÃ LÀM        — file/số lượng, commit hash nếu có
2. CÒN LẠI        — checklist item chưa tick, kèm lý do
3. RỦI RO        — mức 🔴/🟠/🟡 + biện pháp
4. CẦN USER QUYẾT — câu hỏi chặn (đánh số Q1, Q2...)
```

**Bằng tiếng Việt.** Không dán lại file dài. Không báo "xong" nếu chưa tick đủ checklist.

---

## 4. Tham chiếu nhanh

| File | Vai trò |
|---|---|
| `AGENTS.md` | Quy tắc bắt buộc của repo (kiến trúc, code style, bảo mật) |
| `docs/ARCHITECTURE_REQUIREMENTS.md` | Yêu cầu slide (227 trích dẫn) |
| `docs/PARITY.md` | Checklist đầy đủ |
| `docs/PROGRESS.md` | Trạng thái hiện tại |
| `docs/_inventory/*.md` | Kiểm kê thô — **nguồn sự thật cho agent**, đọc theo `offset/limit` |

---

## 5. Checklist trước khi kết thúc phiên làm việc

- [ ] `docs/PROGRESS.md` đã cập nhật (đã làm / còn / rủi ro)
- [ ] `docs/PARITY.md` đã tick đúng các mục thực sự hoàn thành
- [ ] `dotnet build --nologo` = 0 error
- [ ] Không còn tham chiếu Node/npm
- [ ] Không còn đường dẫn ảnh hỏng
- [ ] Không còn secret hard-code trong code (dùng `appsettings` / user-secrets)
- [ ] Commit message đúng chuẩn `feat(prn232): ...`
