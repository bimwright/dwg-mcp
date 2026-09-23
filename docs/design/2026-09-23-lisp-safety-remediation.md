# DWG LISP safety — yêu cầu và chuẩn bị sửa

Ngày: 2026-09-23. Baseline review: `v1.0.0...e48c3b0`.
Trạng thái: đã triển khai chặn thực thi LISP và cải thiện inspection trong working tree; chưa deploy. Owner chọn phương án “Chặn chạy LISP chưa tin cậy; hoàn thiện scanner và thông báo giới hạn trước”.

## Kết quả triển khai đợt này

- Do chưa có cơ chế xác lập script tin cậy hoặc executor cách ly, mọi `dwg_run_lisp` đều trả từ chối, kể cả nguồn được scan `clean`. Không thêm allowlist/hash enrollment hay tùy chọn bỏ qua để giả định trust.
- Server từ chối trước file IO/discovery/dispatch. Plugin từ chối wire command trước document lock; handler cũng từ chối nếu được gọi trực tiếp. `MCPENABLECODE` không bật lại LISP. Batch/ToolBaker vẫn cấm đường này.
- Xóa executor wrapper/queue/polling cũ. Các finding trusted-path, early-result và timeout cleanup được xử lý bằng loại bỏ đường chạy, không phải bằng sửa để tiếp tục thực thi. Muốn mở lại cần thiết kế và nghiệm thu executor mới.
- Scanner đã bắt SHELL dạng bare và literal command; kết quả luôn có `execution_authorized=false`, `safety_assured=false`, cùng giới hạn. Không coi regex là sandbox.
- `send_code` giữ nguyên khả năng C# toàn quyền. Lời nhắc không lách chặn LISP qua C# là hướng dẫn cho agent, không phải cơ chế cách ly kỹ thuật hoặc bảo vệ toàn máy.
- Regression mới đã được chạy fail trước sửa rồi pass sau sửa. Toàn bộ suite: **452/452 pass**. Build plugin AutoCAD **2024: 0 warnings, 0 errors**; **2027: 4 warnings, 0 errors** (Roslyn assembly conflicts và API networking obsolete). Output plugin nằm trong thư mục tạm, không deploy vào host.
- Chưa chạy live acceptance hoặc phát hành. Cần update/restart cả server và plugin để thay đổi có hiệu lực trên máy cài đặt; binary cũ không tự thay đổi.

## Live test để sau — owner xác nhận 2026-09-23

- [ ] **`dwg_send_code`: TEST SAU.** DTO/stdout, ghi bản vẽ + undo, từ chối async/await và host objects, kill-switch, read-only, timeout trên AutoCAD thật.
- [ ] **`dwg_run_lisp`: TEST SAU.** Kiểm chứng từ chối ở MCP và plugin wire, kể cả khi bật `MCPENABLECODE`, restart listener hoặc host đang busy; không có wrapper/lệnh chạy muộn/thay đổi bảo mật host. Tool vẫn bị chặn; chưa có executor LISP an toàn để nghiệm thu thực thi.

Không ghi hai mục này là pass dựa trên unit tests hoặc build. Hoãn live test không có nghĩa cho phép mở lại LISP. Chi tiết tiếp tục tại `docs/testing/fresh-install-checklist.md`.

## Yêu cầu bắt buộc của owner

Tool chạy LISP phải có cơ chế bảo vệ máy người dùng khỏi mã độc và nguy cơ phát tán virus. Đây là điều kiện phát hành, không phải tính năng tùy chọn để làm sau. Đồng bộ với RVT không được đồng nghĩa với đưa mã chưa tin cậy vào AutoCAD mà thiếu ranh giới bảo vệ.

Không được diễn giải kết quả scan `clean` thành “an toàn” hoặc “không có virus”. Không cam kết bảo vệ toàn bộ máy chỉ bằng scanner của MCP.

## Hiện trạng trước sửa đã kiểm chứng tại e48c3b0

- `LispSecurityScanner` tự mô tả là static lint, không phải sandbox. `run_lisp` thực thi trong tiến trình AutoCAD của người dùng, không có môi trường cách ly riêng.
- `.fas/.vlx`, nguồn quá lớn và các mẫu được phân loại `dangerous` bị từ chối; input không đọc được fail closed. `--read-only`, session kill-switch và cấm LISP trong batch đã có.
- `caution` hiện vẫn được chạy. Trong đó có `load`, `eval`, `read` và một số đường COM; nội dung nạp tiếp không được scanner hiện tại kiểm tra đầy đủ.
- Probe chỉ scan, không thực thi: `SHELL notepad.exe` và `(command "_.SHELL" "notepad.exe")` đều ra `clean`; `(startapp "notepad.exe")` ra `dangerous`.
- 431/431 test hiện tại pass. Đây không phải bằng chứng an toàn trước mã độc hoặc nghiệm thu live AutoCAD.

## Bốn finding từ lượt review (bằng chứng trước sửa)

| Mức | Vấn đề và bằng chứng | Hướng sửa / nghiệm thu |
|---|---|---|
| P1 | `RunLispHandler.cs:49-64` load wrapper từ LocalAppData; chưa có xử lý TRUSTEDPATHS/SECURELOAD trong repo. Suy luận từ code và tài liệu Autodesk, chưa live repro. | Đường thực thi tương thích chính sách host. Không tắt SECURELOAD hoặc tự động tin cậy thư mục chứa script tùy ý. Nghiệm thu profile mới với SECURELOAD 1 và 2. |
| P1 | `LispSecurityScanner.cs:43,62` bỏ lọt SHELL trực tiếp và qua `command`; đã probe scanner. | Kiểm tra cả ngữ nghĩa LISP và command-line; thêm các ca regression cho đường chạy OS trực tiếp, tiền tố lệnh và gọi gián tiếp. Không coi mở rộng regex là sandbox. |
| P2 | `RunLispHandler.cs:121-125` dùng file tồn tại + 50 ms làm dấu hoàn tất. Probe actual handler với host double trả `ok=true,result=null` sau 74 ms, trước writer 500 ms ghi lỗi. | Công bố kết quả nguyên tử sau close hoặc completion protocol tương đương; test writer chậm, kết quả lớn, lỗi muộn. |
| P2 | `RunLispHandler.cs:84-87` xóa wrapper sau timeout dù command còn có thể queued. Probe xác nhận wrapper biến mất sau 30 s. | Cleanup theo trạng thái hoàn tất/hủy thực tế; không để bare command tiếp tục độc lập khi bước chuẩn bị thất bại. Nghiệm thu busy host và thao tác nhả queue. |

## Ranh giới bảo vệ đề xuất — cần chốt trước implementation

1. **Mã không tin cậy không được chạy tùy ý trong AutoCAD đang dùng.** Nếu chưa có môi trường cách ly đủ khả năng hạn chế filesystem/network/process, từ chối đường này. Muốn hỗ trợ LISP bên ngoài bất kỳ phải thiết kế môi trường thực thi cách ly riêng; scanner không thay thế được ranh giới đó.
2. **Tách trust khỏi scan.** Scan là bằng chứng phụ trợ; `clean`, extension `.lsp`, việc agent sinh code, hoặc một checkbox xác nhận không tự thiết lập ranh giới chống phát tán. Đường chạy script được kiểm soát cần policy rõ về nguồn, nội dung, phụ thuộc và quyền được phép.
3. **Fail closed khi không xác định được hành vi.** Không tự động cho chạy chỉ với cảnh báo đối với dynamic evaluation, staged loading hoặc command/COM không xác định. Ưu tiên tập thao tác được cho phép và kiểm chứng rõ; không chỉ danh sách từ khóa cấm.
4. **Gắn kiểm tra với đúng nội dung sẽ chạy.** Đọc snapshot, nhận diện bằng hash, và bảo đảm thực thi chính snapshot đã kiểm tra; kiểm soát phụ thuộc. Không scan một đường dẫn rồi để plugin load nội dung có thể đã đổi. Snapshot/hash chỉ bảo đảm tính toàn vẹn, không chứng minh script an toàn.
5. **Kiểm soát các tác động ngoài bản vẽ.** Phải xét process/shell, native/.NET loading, filesystem và network shares, registry/startup persistence, network và COM. Nếu không cưỡng chế được giới hạn trong host thì từ chối mã chưa tin cậy, không quảng cáo đang sandbox.
6. **Không hướng dẫn lách từ chối.** Tool response/server instructions không được gợi ý chạy lại payload bị chặn qua `dwg_send_code`, batch, ToolBaker hoặc đổi dạng nội dung. `send_code` vẫn là escape hatch có toàn quyền và phải được mô tả riêng; scanner LISP không bảo vệ được các đường thực thi khác.

Các mục trên là hướng thiết kế đề xuất để đáp ứng yêu cầu owner, chưa phải mô tả capability đã có. Không tự thêm trust path rộng hoặc thay đổi thiết lập bảo mật Windows/AutoCAD để làm test pass.

## Điều kiện nghiệm thu trước phát hành

- [x] Chốt policy đợt này: không có cơ chế trust/isolation thì không chạy LISP; công khai giới hạn bảo vệ và C# toàn quyền.
- [x] Server và handler từ chối đầu vào; batch/ToolBaker không dispatch LISP. Regression chứng minh server không discovery/đọc file và handler không queue.
- [ ] Regression cho SHELL, staged load, eval/read, compiled payload, COM, persistence, đổi file sau scan và dependency chưa kiểm tra. Dùng doubles/canaries; không chạy mã độc trên máy người dùng.
- [ ] Chặn hoặc cách ly được tác động process/filesystem/network/registry theo policy đã chốt. Không lấy việc pass scanner làm tiêu chí thay thế.
- [x] Chặn đường gây ba lỗi lifecycle/trusted-path bằng loại bỏ executor; sửa scanner SHELL. Đây không phải nghiệm thu một executor LISP đang hoạt động.
- [ ] Live AutoCAD trên bản vẽ dùng thử/profile phù hợp: idle, busy, prompt, timeout, cancel, SECURELOAD 1/2, kill-switch, read-only.
- [x] Đổi wording `clean`/warnings; cập nhật tool descriptions, server instructions, README bốn ngôn ngữ, architecture, changelog và checklist. Không nhận là antivirus/sandbox.

## Tham chiếu

- `src/server/LispSecurityScanner.cs`
- `src/server/Tools/CodeTools.cs`
- `src/shared/Handlers/RunLispHandler.cs`
- `docs/testing/fresh-install-checklist.md`
- [Autodesk SECURELOAD](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-541566C6-2738-49DD-87C3-C1490E924A02.htm)
- [Autodesk SHELL](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-675A6BDA-681C-445E-9A32-B8D3713F258A.htm)

Các mục snapshot/dependency và cách ly tác động OS còn unchecked là điều kiện cho **executor tương lai**, không phải capability của bản đang chặn thực thi. Live acceptance và deployment của bản chặn vẫn chưa thực hiện.

Thứ tự tiếp theo: nghiệm thu/deploy bản chặn khi phù hợp, sau đó response budget/spill, rồi model digest + text mapping theo `2026-09-22-model-reading-stack.md`. Mở lại LISP là một đợt thiết kế riêng.
